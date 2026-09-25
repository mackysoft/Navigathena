using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Runtime.Navigation;
using MackySoft.Navigathena.Runtime.Screens;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed class CallCoordinator
    {
        public bool OwnsCall (NavigationEntryId owner, PresentationId presentation)
        {
            lock (sync)
            {
                return calls.Values.Any(call => call.Owner == owner && call.OwnerPresentation == presentation && !call.Completion.IsCompleted);
            }
        }
        private readonly object sync = new();
        private readonly Dictionary<Guid, CallRecord> calls = new();
        private readonly ScreenRuntime screens;
        private readonly INavigationStateSource state;
        private readonly Func<NavigationRequest, NavigationOperation> submit;
        private readonly Func<Task?> captureReservationRelease;
        private bool closed;

        public CallCoordinator (ScreenRuntime screens, INavigationStateSource state, Func<NavigationRequest, NavigationOperation> submit, Func<Task?> captureReservationRelease)
        {
            this.screens = screens;
            this.state = state;
            this.submit = submit;
            this.captureReservationRelease = captureReservationRelease;
        }

        public Task InvokeAsync (NavigationEntryId? owner, PresentationId? presentation, RegionInstanceId target, Route route, CancellationToken cancellationToken, NavigationOptions? options)
        {
            return BeginCall<object?>(owner, presentation, target, route, cancellationToken, options, requiresAnswer: false).Completion;
        }

        public Task<TResult> InvokeAsync<TResult> (NavigationEntryId? owner, PresentationId? presentation, RegionInstanceId target, Route<TResult> route, CancellationToken cancellationToken, NavigationOptions? options)
        {
            CallRecord<TResult> call = BeginCall<TResult>(owner, presentation, target, route, cancellationToken, options, requiresAnswer: true);
            return call.Answer;
        }

        private CallRecord<TResult> BeginCall<TResult> (NavigationEntryId? owner, PresentationId? presentation, RegionInstanceId target, NavigationRoute route, CancellationToken token, NavigationOptions? options, bool requiresAnswer)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }
            if (NavigationCallbackScope.IsExecuting)
            {
                throw new InvalidOperationException("Start owned work to invoke a screen after the lifecycle callback completes.");
            }
            token.ThrowIfCancellationRequested();
            NavigationState snapshot = state.Current;
            Guid? parent = owner.HasValue ? snapshot.GetEntry(owner.Value).CallId : null;
            if (owner.HasValue && snapshot.GetRegion(target).Entries.LastOrDefault() != owner && snapshot.GetEntry(owner.Value).RegionId == target)
            {
                throw new InvalidOperationException("Only the current screen can invoke within its region.");
            }
            CallRecord<TResult> call = new(owner, presentation, target, parent, requiresAnswer);
            lock (sync)
            {
                if (closed)
                {
                    throw new NavigationRuntimeClosedException();
                }
                calls.Add(call.Id, call);
            }
            try
            {
                SourcePrecondition? source = owner.HasValue ? new SourcePrecondition(owner.Value, presentation!.Value) : null;
                call.Opening = submit(new NavigationRequest(NavigationOperationKind.Push, target, new NavigationDestinationTree<NavigationRoute>(route), options, CancellationToken.None, source)
                {
                    CallChange = new CallChange(call.Id, CallChangeKind.Open) { Owner = owner }
                });
                call.Cancellation = token.Register(() => RequestCancellation(call));
                _ = ObserveOpeningAsync(call);
            }
            catch (Exception exception)
            {
                call.Opened.TrySetException(exception);
                call.Fail(exception);
                Remove(call);
                throw;
            }
            return call;
        }

        private async Task ObserveOpeningAsync (CallRecord call)
        {
            try
            {
                NavigationResult result = await call.Opening!.WaitForRuntimeAsync();
                RequireCommitted(result);
                call.Opened.TrySetResult(result);
            }
            catch (Exception exception)
            {
                call.Opened.TrySetException(exception);
                Fail(call, exception, ScreenCallFailureStage.Opening);
                if (!call.Published)
                {
                    Remove(call);
                }
            }
        }

        public IScreenCallScope Bind (NavigationEntry entry, PresentationId presentation, Func<bool> valid)
            => new CallScope(this, entry, presentation, valid);

        private ScreenCall<TResult> Connect<TResult> (NavigationEntry entry, PresentationId presentation, Func<bool> valid)
        {
            CallRecord<TResult> call;
            lock (sync)
            {
                call = entry.CallId is Guid id && calls.TryGetValue(id, out CallRecord? record) && record is CallRecord<TResult> { RequiresAnswer: true } typed
                    ? typed : throw new NavigationConfigurationException("The screen has no call matching its answer contract.");
            }
            void Validate ()
            {
                if (NavigationCallbackScope.IsExecuting || !valid())
                {
                    throw new InvalidOperationException("The reply capability requires a live screen activity or owned work outside a lifecycle callback.");
                }
                lock (sync)
                {
                    if (closed || call.Ending || call.Closing || call.Completion.IsCompleted)
                    {
                        throw new InvalidOperationException("The screen call has already ended or is ending.");
                    }
                }
            }
            return new ScreenCall<TResult>(value =>
            {
                Validate();
                lock (sync)
                {
                    if (call.Closing || call.Ending || call.Completion.IsCompleted)
                    {
                        throw new InvalidOperationException("The screen call has already accepted a terminal request.");
                    }
                    call.Value = value;
                    call.Closing = true;
                }
                Close(call, new SourcePrecondition(entry.Id, presentation), CallEndReason.Answer);
            }, () =>
            {
                Validate();
                lock (sync)
                {
                    if (call.Closing || call.Ending || call.Completion.IsCompleted)
                    {
                        throw new InvalidOperationException("The screen call has already accepted a terminal request.");
                    }
                    call.Closing = true;
                }
                Close(call, new SourcePrecondition(entry.Id, presentation), CallEndReason.Close);
            }, (route, reset, options) =>
            {
                Validate();
                return submit(new NavigationRequest(reset ? NavigationOperationKind.Reset : NavigationOperationKind.Replace, call.Region,
                    new NavigationDestinationTree<Route<TResult>>(route), options, CancellationToken.None, new SourcePrecondition(entry.Id, presentation, call.Region, entry.Id))
                {
                    CallChange = new CallChange(call.Id, reset ? CallChangeKind.Reset : CallChangeKind.Replace) { Owner = call.Owner }
                });
            });
        }

        private void RequestCancellation (CallRecord call)
        {
            lock (sync)
            {
                if (closed || call.Ending || call.Closing || call.Completion.IsCompleted)
                {
                    return;
                }
                call.Closing = true;
            }
            call.Opening?.TryRequestCancellation();
            _ = CancelAfterOpeningAsync(call);
        }

        private async Task CancelAfterOpeningAsync (CallRecord call)
        {
            try
            {
                await call.Opened.Task;
                if (!state.Current.Entries.Values.Any(entry => entry.CallId == call.Id))
                {
                    return;
                }
                Close(call, null, CallEndReason.Cancellation);
            }
            catch (Exception exception)
            {
                call.Fail(exception);
            }
        }

        private void Close (CallRecord call, SourcePrecondition? source, CallEndReason reason)
            => _ = CloseAsync(call, source, reason);

        private async Task CloseAsync (CallRecord call, SourcePrecondition? source, CallEndReason reason)
        {
            try
            {
                while (true)
                {
                    lock (sync)
                    {
                        if (closed || call.Ending || call.Discarded)
                        {
                            return;
                        }
                    }
                    Task? release = captureReservationRelease();
                    NavigationOperation operation = submit(new NavigationRequest(NavigationOperationKind.Back, call.Region, null, null, CancellationToken.None, source)
                    {
                        CallChange = new CallChange(call.Id, CallChangeKind.Close) { EndReason = reason }
                    });
                    NavigationResult result = await operation.WaitForRuntimeAsync();
                    if (result.Kind == NavigationResultKind.Conflict && release is not null)
                    {
                        await release;
                        continue;
                    }
                    RequireCommitted(result);
                    return;
                }
            }
            catch (Exception exception)
            {
                Fail(call, exception, ScreenCallFailureStage.Returning);
                screens.ReportEquipmentFailure("A screen call could not finish: " + exception.Message);
            }
        }

        public void Commit (NavigationState candidate, CallChange? change, NavigationOperationKind operation)
        {
            lock (sync)
            {
                HashSet<Guid> present = candidate.Entries.Values.Where(entry => entry.CallId.HasValue).Select(entry => entry.CallId!.Value).ToHashSet();
                foreach (CallRecord call in calls.Values)
                {
                    call.Published |= present.Contains(call.Id);
                    if (!call.Published || present.Contains(call.Id))
                    {
                        continue;
                    }
                    if (!call.Ending)
                    {
                        call.EndReason = change?.Id == call.Id && change.Kind == CallChangeKind.Close
                            ? change.EndReason
                            : operation == NavigationOperationKind.Back && change is null ? CallEndReason.Back : CallEndReason.Interrupted;
                        call.Ending = true;
                    }
                    if (call.Owner is NavigationEntryId owner && !candidate.Entries.ContainsKey(owner))
                    {
                        // Release child waits before waiting for the removed owner's work.
                        call.CancelOwner();
                    }
                }
            }
        }

        public void EndBinding (NavigationEntryId owner, PresentationId presentation)
        {
            lock (sync)
            {
                foreach (CallRecord call in calls.Values.Where(call => call.Owner == owner && call.OwnerPresentation == presentation))
                {
                    call.CancelOwner();
                }
            }
        }

        public void FailReturning (Exception exception, IReadOnlyCollection<Guid> affected)
        {
            lock (sync)
            {
                foreach (CallRecord call in calls.Values.Where(call => call.Ending && !call.Discarded && affected.Contains(call.Id)))
                {
                    Fail(call, exception, ScreenCallFailureStage.Returning);
                }
            }
        }

        private void Fail (CallRecord call, Exception exception, ScreenCallFailureStage stage)
            => call.Fail(exception is OperationCanceledException ? exception : new ScreenCallException(call.Id, stage, call.Answered,
                exception is NavigationException failure ? failure.FinalSnapshot : state.Current, exception));

        public void TrackRetirement (ScreenInstance screen, Task retirement)
        {
            lock (sync)
            {
                Guid? id = screen.Entry.CallId;
                while (id.HasValue && calls.TryGetValue(id.Value, out CallRecord? call))
                {
                    call.Retirements.Add(retirement);
                    id = call.Parent;
                }
            }
        }

        public async ValueTask SettleAsync ()
        {
            CallRecord[] pending;
            lock (sync)
            {
                pending = calls.Values.Where(call => call.Ending).ToArray();
            }
            foreach (CallRecord call in pending)
            {
                if (!call.Completion.IsCompleted && !call.Discarded && call.Owner is NavigationEntryId owner
                    && screens.Find(state.Current.GetPresentation(owner))?.IsActive != true)
                {
                    continue;
                }
                try
                {
                    await WaitForRetirementAsync(call);
                    bool otherCalls;
                    lock (sync)
                    {
                        otherCalls = calls.Values.Any(other => other.Id != call.Id && other.Owner == call.Owner
                            && other.OwnerPresentation == call.OwnerPresentation && !other.Completion.IsCompleted);
                    }
                    if (!otherCalls && call.Owner is NavigationEntryId caller
                        && state.Current.Presentations.TryGetValue(caller, out PresentationState? presentation)
                        && screens.Find(presentation) is ScreenInstance screen && screen.Id == call.OwnerPresentation)
                    {
                        await screen.ReleasePreviousInputAsync(retainForCalls: false);
                    }
                    call.Finish(state.Current);
                }
                catch (Exception exception)
                {
                    Fail(call, exception, ScreenCallFailureStage.ResourceCleanup);
                }
                Remove(call);
            }
        }

        private Task WaitForRetirementAsync (CallRecord call)
        {
            lock (sync)
            {
                return Task.WhenAll(call.Retirements.ToArray());
            }
        }

        public void Shutdown ()
        {
            CallRecord[] pending;
            lock (sync)
            {
                closed = true;
                pending = calls.Values.ToArray();
                foreach (CallRecord call in pending)
                {
                    call.CancelOwner();
                }
                calls.Clear();
            }
            foreach (CallRecord call in pending)
            {
                call.Cancellation.Dispose();
            }
        }

        private void Remove (CallRecord call)
        {
            lock (sync)
            {
                calls.Remove(call.Id);
            }
            call.Cancellation.Dispose();
        }

        private static void RequireCommitted (NavigationResult result)
        {
            if (!result.DestinationCommitted)
            {
                throw new InvalidOperationException("The screen call navigation was " + result.Kind + ".");
            }
        }

        private sealed class CallScope : IScreenCallScope
        {
            private readonly CallCoordinator owner;
            private readonly NavigationEntry entry;
            private readonly PresentationId presentation;
            private readonly Func<bool> valid;

            public CallScope (CallCoordinator owner, NavigationEntry entry, PresentationId presentation, Func<bool> valid)
            {
                this.owner = owner;
                this.entry = entry;
                this.presentation = presentation;
                this.valid = valid;
            }

            public ScreenCall<TResult> Connect<TResult> () => owner.Connect<TResult>(entry, presentation, valid);
        }
    }
}
