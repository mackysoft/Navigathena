using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Navigation;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal sealed class ScreenInstance
    {
        private readonly SemaphoreSlim lifecycle = new(1, 1);
        private readonly INavigationStateSource state;
        private IScreenNavigation navigation;
        private readonly Action<ScreenInstance, string> reportLoss;
        private readonly Func<ScreenInstance, ValueTask> endUser;
        private readonly Action<ScreenInstance, object> claimHandler;
        private readonly TaskCompletionSource<object?> prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object terminationSync = new();
        private readonly List<ResourceScope> inputResources = new();
        private IScreenLifecycleInvocation? handler;
        private bool handlerActive;
        private bool handlerTerminated;
        private CancellationTokenSource? activity;
        private Task? stoppingActivity;
        private Task? termination;
        private int users;
        private TaskCompletionSource<object?> unused = CompletedSignal();
        private bool initialized;
        private volatile bool activityReady;
        private int ending;
        private Exception? activityCancellationFailure;
        private readonly IScreenActivityRuntime runtime;
        private ScreenWorkCollection work;
        internal ScreenPreparationReason? PreparationReason
        {
            get; set;
        }
        internal bool NavigationSettled
        {
            get; set;
        }
        internal bool HasWork => work.HasPending;
        internal bool HasPendingUse => HasWork || runtime.OwnsCall(Entry.Id, Id);
        internal void StartReadyWork () => work.StartReady();
        internal void PostNavigation (Action request)
        {
            CancellationToken token = activity?.Token ?? throw new InvalidOperationException("Posting navigation requires a current activity.");
            work.Add(_ =>
            {
                if (!token.IsCancellationRequested)
                {
                    request();
                }
                return default;
            });
        }

        public ScreenInstance (NavigationEntry entry, PresentationContext context, RegionRouteDefinition definition, ScreenDefinition construction, ViewRegistry views, INavigationStateSource state, Action<ScreenInstance, string> reportLoss, Func<ScreenInstance, ValueTask> endUser, Action<ScreenInstance, object> claimHandler, IScreenActivityRuntime runtime)
        {
            Entry = entry;
            Definition = construction;
            Id = context.PresentationId;
            this.state = state;
            navigation = context.Navigation;
            this.reportLoss = reportLoss;
            this.endUser = endUser;
            this.claimHandler = claimHandler;
            this.runtime = runtime;
            work = new ScreenWorkCollection(this, runtime);
            Lifetime = new ResourceScope(() => this.endUser(this), reason => this.reportLoss(this, reason));
            Creation = new ManagedScreenCreationContext(entry, Lifetime, views, definition);
        }

        public NavigationEntry Entry
        {
            get; private set;
        }
        public ScreenDefinition Definition
        {
            get;
        }
        public PresentationId Id
        {
            get;
        }
        public ScreenInstance? Parent
        {
            get; set;
        }
        public int OwnershipDepth => Parent is null ? 0 : Parent.OwnershipDepth + 1;
        public ManagedScreenCreationContext Creation
        {
            get;
        }
        internal object? LifecycleHandler => handler?.Handler;
        public ResourceScope Lifetime
        {
            get;
        }
        public bool IsActive => activityReady && activity is not null && !activity.IsCancellationRequested;
        public bool IsEnding => Volatile.Read(ref ending) != 0 || Lifetime.EndingToken.IsCancellationRequested;
        public bool IsTerminated
        {
            get; private set;
        }
        public ViewPresentation Presentation
        {
            get; private set;
        }

        public IDisposable Use ()
        {
            lock (terminationSync)
            {
                if (IsEnding)
                {
                    throw new InvalidOperationException("The screen is ending.");
                }

                if (users++ == 0)
                {
                    unused = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                return new Usage(this);
            }
        }

        public void MarkEnding () => Interlocked.Exchange(ref ending, 1);

        public void InvalidateActivity ()
        {
            activityReady = false;
            try
            {
                activity?.Cancel();
            }
            catch (Exception exception)
            {
                activityCancellationFailure = exception;
            }
            List<Exception> failures = new();
            foreach (ViewRegistration view in Creation.Registrations)
            {
                try
                {
                    if (view.Adapter.IsAlive)
                    {
                        view.Apply(new ViewPresentation(view.Adapter.Presentation.OutputEnabled, false, view.Adapter.Presentation.Order));
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Some native inputs could not be closed.", failures);
            }
        }

        public bool DependsOn (ScreenInstance ancestor) => Parent is not null && (ReferenceEquals(Parent, ancestor) || Parent.DependsOn(ancestor));

        public void NotifyChange (PresentationChangeContext context)
        {
            if (IsEnding)
            {
                return;
            }
            if (LifecycleHandler is INavigationChangeHandler changeHandler)
            {
                changeHandler.OnNavigationChanged(context);
            }
        }
        public IScreenNavigation ConnectNavigation (Func<bool> connected) => new ActivityNavigation(navigation, () => connected() && IsActive, state);

        public async ValueTask PrepareAsync (CancellationToken cancellationToken, ScreenPreparationReason reason = ScreenPreparationReason.NewEntry)
        {
            await lifecycle.WaitAsync();
            try
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime.EndingToken);
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await Definition.CreateAsync(Creation, cancellation.Token);
                    IScreenLifecycleInvocation created = Creation.Handler!;
                    claimHandler(this, created.Handler);
                    handler = created;
                }
                finally
                {
                    await Lifetime.CloseAsync();
                }
                cancellation.Token.ThrowIfCancellationRequested();
                await NavigationCallbackScope.RunAsync(() => handler.InitializeAsync(cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                initialized = true;
                await PrepareInputAsync(cancellation.Token, reason);
            }
            finally
            {
                lifecycle.Release();
                prepared.TrySetResult(null);
            }
        }

        public async ValueTask RebindAsync (NavigationEntry entry, PresentationContext context, CancellationToken cancellationToken, ScreenPreparationReason reason)
            => await RebindAsync(entry, context.Navigation, cancellationToken, reason);

        internal IScreenNavigation Navigation => navigation;

        internal async ValueTask RebindAsync (NavigationEntry entry, IScreenNavigation nextNavigation, CancellationToken cancellationToken, ScreenPreparationReason reason = ScreenPreparationReason.Recovery)
        {
            await lifecycle.WaitAsync();
            try
            {
                if (IsActive || IsEnding || !initialized)
                {
                    throw new InvalidOperationException("Only a stopped, initialized screen can display another history entry.");
                }
                if (entry.Id != Entry.Id)
                {
                    await work.StopAsync();
                    work = new ScreenWorkCollection(this, runtime, entry);
                }
                Entry = entry;
                navigation = nextNavigation;
                await PrepareInputAsync(cancellationToken, reason);
            }
            finally
            {
                lifecycle.Release();
            }
        }

        private async ValueTask PrepareInputAsync (CancellationToken cancellationToken, ScreenPreparationReason reason)
        {
            ResourceScope input = new(() => endUser(this), reason => reportLoss(this, reason));
            inputResources.Add(input);
            ScreenPreparationContext preparation = new(Entry, input.Context, reason);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime.EndingToken, input.EndingToken);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await NavigationCallbackScope.RunAsync(() => handler!.PrepareAsync(Entry.Route, preparation, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                PreparationReason = reason;
            }
            finally
            {
                await input.CloseAsync();
            }
        }

        public async ValueTask ReleasePreviousInputAsync (bool retainForCalls = true)
        {
            await lifecycle.WaitAsync();
            try
            {
                if (HasWork || (retainForCalls && runtime.OwnsCall(Entry.Id, Id)) || IsEnding)
                {
                    return;
                }
                while (inputResources.Count > 1)
                {
                    await inputResources[0].DisposeAsync();
                    inputResources.RemoveAt(0);
                }
            }
            finally
            {
                lifecycle.Release();
            }
        }

        public async ValueTask ActivateAsync (ScreenActivationReason reason = ScreenActivationReason.Recovery)
        {
            await lifecycle.WaitAsync();
            try
            {
                if (IsEnding || !initialized)
                {
                    throw new InvalidOperationException("The screen is not available for activity.");
                }

                if (IsActive)
                {
                    return;
                }

                if (stoppingActivity is not null)
                {
                    await stoppingActivity;
                }

                CancellationTokenSource current = new();
                activity = current;
                stoppingActivity = null;
                int workCheckpoint = work.Count;
                ActivityNavigation bound = new(navigation, () => activityReady && !current.IsCancellationRequested && !IsEnding, state,
                    () => !current.IsCancellationRequested && !IsEnding);
                try
                {
                    ScreenWorkCollection binding = work;
                    ScreenActivityContext context = new(Entry.Id, Entry.RegionId, bound, current.Token,
                        runtime.BindCall(Entry, Id, () => IsActive && !current.IsCancellationRequested),
                        callback =>
                        {
                            if (current.IsCancellationRequested || IsEnding || !ReferenceEquals(binding, work))
                            {
                                throw new InvalidOperationException("The activity that started this work is no longer valid.");
                            }
                            return binding.Add(callback);
                        }, !runtime.HasActivated(Entry.Id), reason, PreparationReason);
                    current.Token.ThrowIfCancellationRequested();
                    handlerActive = true;
                    await NavigationCallbackScope.RunAsync(() => handler!.ActivateAsync(Entry.Route, context));
                    current.Token.ThrowIfCancellationRequested();
                    activityReady = true;
                }
                catch
                {
                    work.CancelPendingSince(workCheckpoint);
                    await StopActivityAsync();
                    throw;
                }
            }
            finally
            {
                lifecycle.Release();
            }
        }

        public async ValueTask DeactivateAsync ()
        {
            await lifecycle.WaitAsync();
            try
            {
                await StopActivityAsync();
            }
            finally
            {
                lifecycle.Release();
            }
        }

        public async ValueTask CloseActivityAsync ()
        {
            Exception? failure = null;
            try
            {
                InvalidateActivity();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                await DeactivateAsync();
            }
            catch (Exception exception)
            {
                if (failure is not null)
                {
                    throw new AggregateException(failure, exception);
                }
                throw;
            }
            if (failure is not null)
            {
                throw failure;
            }
        }

        private ValueTask StopActivityAsync ()
        {
            if (activity is null)
            {
                return default;
            }

            return new ValueTask(stoppingActivity ??= StopActivityCoreAsync(activity));
        }

        private async Task StopActivityCoreAsync (CancellationTokenSource current)
        {
            activityReady = false;
            // Invalidate the navigation generation before invoking cancellation or user callbacks.
            Exception? cancellationFailure = activityCancellationFailure;
            try
            {
                current.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }

            List<Exception> failures = new();
            if (cancellationFailure is not null)
            {
                failures.Add(cancellationFailure);
            }
            if (handlerActive)
            {
                try
                {
                    await NavigationCallbackScope.RunAsync(handler!.DeactivateAsync);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Screen activity did not stop safely.", failures);
            }
            handlerActive = false;
        }

        public void Apply (ViewPresentation presentation)
        {
            if (IsEnding && presentation.OutputEnabled)
            {
                throw new InvalidOperationException("An ending screen cannot be published.");
            }

            foreach (ViewRegistration view in Creation.Registrations)
            {
                view.Apply(presentation);
            }

            Presentation = presentation;
        }

        public PresentationStateCapture? CaptureState ()
        {
            IScreenStateCapture? capture = LifecycleHandler as IScreenStateCapture;
            return capture is null ? null : new PresentationStateCapture(Entry.Id, Id, capture.CaptureState());
        }

        public ValueTask TerminateAsync ()
        {
            Interlocked.Exchange(ref ending, 1);
            TaskCompletionSource<object?> completion;
            lock (terminationSync)
            {
                if (termination is not null)
                {
                    return new ValueTask(termination);
                }

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                termination = completion.Task;
            }

            _ = FinishTerminationAsync(completion);
            return new ValueTask(completion.Task);
        }

        private async Task FinishTerminationAsync (TaskCompletionSource<object?> completion)
        {
            try
            {
                await TerminateCoreAsync();
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }

        private async Task TerminateCoreAsync ()
        {
            await prepared.Task;
            await work.StopAsync();
            await unused.Task;
            await lifecycle.WaitAsync();
            try
            {
                List<Exception> inputFailures = new();
                try
                {
                    InvalidateActivity();
                }
                catch (Exception exception)
                {
                    inputFailures.Add(exception);
                }

                await StopActivityAsync();
                if (handler is not null && !handlerTerminated)
                {
                    await NavigationCallbackScope.RunAsync(handler.TerminateAsync);
                    handlerTerminated = true;
                }

                Creation.TransitionView?.Release();
                Creation.ReleasePresentation();
                // Owned services may reference input resources and scene views during their disposal.
                await Lifetime.ReleaseOwnedAsync();
                for (int i = inputResources.Count - 1; i >= 0; i--)
                {
                    await inputResources[i].DisposeAsync();
                }
                inputResources.Clear();
                await Lifetime.DisposeAsync();
                IsTerminated = true;
                if (inputFailures.Count > 0)
                {
                    throw new AggregateException("Screen ended, but native input closure failed.", inputFailures);
                }
            }
            finally
            {
                lifecycle.Release();
            }
        }

        private static TaskCompletionSource<object?> CompletedSignal ()
        {
            TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            completion.SetResult(null);
            return completion;
        }

        private sealed class Usage : IDisposable
        {
            private ScreenInstance? owner;
            public Usage (ScreenInstance owner) => this.owner = owner;
            public void Dispose ()
            {
                ScreenInstance? current = Interlocked.Exchange(ref owner, null);
                if (current is null)
                {
                    return;
                }

                lock (current.terminationSync)
                {
                    if (--current.users == 0)
                    {
                        current.unused.TrySetResult(null);
                    }
                }
            }
        }
    }
}
