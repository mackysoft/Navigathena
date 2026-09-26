using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Composition;
using MackySoft.Navigathena.Runtime.Messaging;
using MackySoft.Navigathena.Runtime.Navigation;
using MackySoft.Navigathena.Runtime.Planning;
using MackySoft.Navigathena.Runtime.Publication;
using MackySoft.Navigathena.Runtime.Reservations;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.State;

namespace MackySoft.Navigathena.Runtime.Execution
{

    internal sealed class NavigationRuntime : INavigationClient, INavigationStateSource, INavigationRecoveryClient, INavigationLossSink, INavigationHostIncidentSink, ISourceNavigationRequestPort, IAsyncDisposable
    {
        private readonly NavigationDefinition definition;
        private readonly IPresentationRealizer realizer;
        private readonly NavigationStateStore state;
        private readonly NavigationPlanner planner;
        private readonly PresentationUpdateFactory updates;
        private readonly ReservationCoordinator reservations = new();
        private readonly SemaphoreSlim commitGate = new(1, 1);
        private readonly CancellationTokenSource shutdown = new();
        private readonly NavigationMailbox mailbox = new();
        private readonly NotificationBarrier notificationBarrier = new();
        private readonly IReadOnlyList<INavigationCommitObserver> observers;
        private readonly Func<NavigationResult, ValueTask>? operationCompleted;
        private readonly Task mailboxPump;
        private readonly object operationSync = new();
        private readonly object hostIncidentAdmissionSync = new();
        private readonly HashSet<Task> activeOperations = new();
        private readonly HashSet<NavigationIncidentId> resolvedHostIncidents = new();
        private readonly HashSet<NavigationIncidentId> recoveryReservations = new();
        private readonly HashSet<HostIncidentMailboxRequest> acceptedHostIncidentReports = new();
        private readonly Dictionary<HostIncidentMailboxRequest, TaskCompletionSource<HostIncidentReportResult>> deferredHostIncidentReports = new();
        private long hostIncidentAdmissionOrder;
        private long hostIncidentAdmissionWatermark;
        private long hostIncidentProcessedWatermark;
        private NavigationIncidentId? hostRecoveryInProgress;
        private long hostRecoveryStartAdmissionOrder;
        private int closed;
        private CallCoordinator? calls;
        Task ISourceNavigationRequestPort.InvokeAsync (NavigationEntryId owner, PresentationId presentation, RegionInstanceId target, Route route, CancellationToken cancellationToken, NavigationOptions? options)
            => (calls ?? throw new InvalidOperationException("The protocol-only host cannot invoke screens.")).InvokeAsync(owner, presentation, target, route, cancellationToken, options);
        Task<TResult> ISourceNavigationRequestPort.InvokeAsync<TResult> (NavigationEntryId owner, PresentationId presentation, RegionInstanceId target, Route<TResult> route, CancellationToken cancellationToken, NavigationOptions? options)
            => (calls ?? throw new InvalidOperationException("The protocol-only host cannot invoke screens.")).InvokeAsync(owner, presentation, target, route, cancellationToken, options);

        public NavigationRuntime (NavigationDefinition definition, IPresentationRealizer realizer, IReadOnlyList<INavigationCommitObserver> observers, Func<NavigationResult, ValueTask>? operationCompleted = null)
        {
            this.definition = definition;
            this.realizer = realizer;
            state = new NavigationStateStore(NavigationState.CreateEmpty(definition.RootRegionId));
            planner = new NavigationPlanner(definition, realizer is ScreenRuntime screens ? screens.ResolveScreens : null);
            updates = new PresentationUpdateFactory(definition, this, this);
            this.observers = observers;
            this.operationCompleted = operationCompleted;
            if (realizer is ScreenRuntime screenRuntime)
            {
                calls = new CallCoordinator(screenRuntime, this, Submit, reservations.CaptureRelease);
                screenRuntime.Calls = calls;
            }
            mailboxPump = PumpMailboxAsync();
        }

        public RegionInstanceId Root => state.Current.RootRegionInstanceId;
        public NavigationState Current => state.Current;
        public Task InvokeAsync (RegionInstanceId target, Route route, CancellationToken cancellationToken = default, NavigationOptions? options = null)
            => (calls ?? throw new InvalidOperationException("The host does not own screens.")).InvokeAsync(null, null, target, route, cancellationToken, options);
        public Task<TResult> InvokeAsync<TResult> (RegionInstanceId target, Route<TResult> route, CancellationToken cancellationToken = default, NavigationOptions? options = null)
            => (calls ?? throw new InvalidOperationException("The host does not own screens.")).InvokeAsync(null, null, target, route, cancellationToken, options);
        public ValueTask<NavigationState> WaitForChangeAsync (long observedRevision, CancellationToken cancellationToken = default) => state.WaitForChangeAsync(observedRevision, cancellationToken);

        public NavigationOperation Push<TRoute> (RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Submit(new NavigationRequest(NavigationOperationKind.Push, target, destination, options, CancellationToken.None, null));
        public NavigationOperation ReplaceFrom<TRoute> (RegionInstanceId region, HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Submit(new NavigationRequest(NavigationOperationKind.Replace, region, destination, options, CancellationToken.None, null)
        {
            ReplacementTarget = target ?? throw new ArgumentNullException(nameof(target))
        });
        public NavigationOperation Reset<TRoute> (RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Submit(new NavigationRequest(NavigationOperationKind.Reset, target, destination, options, CancellationToken.None, null));
        public NavigationOperation Back (RegionInstanceId target, BackOptions? options = null) => Submit(new NavigationRequest(NavigationOperationKind.Back, target, null, options?.ToNavigationOptions(), CancellationToken.None, null));
        public NavigationOperation Reload (RegionInstanceId target, ReloadOptions? options = null) => Submit(new NavigationRequest(NavigationOperationKind.Reload, target, null, (options ?? new ReloadOptions()).ToNavigationOptions(), CancellationToken.None, null));
        public NavigationOperation Clear (RegionInstanceId target) => Submit(new NavigationRequest(NavigationOperationKind.Clear, target, null, null, CancellationToken.None, null));

        public ValueTask<NavigationResult> RecoverAsync (NavigationIncidentId incidentId, CancellationToken cancellationToken = default)
        {
            return ObserveCompletionAsync(mailbox.Write(new NavigationRecoveryMailboxRequest(incidentId, cancellationToken), Current), cancellationToken);
        }

        public ValueTask<PresentationLossResult> ReportAsync (PresentationLoss loss, CancellationToken cancellationToken = default)
        {
            if (loss is null)
            {
                throw new ArgumentNullException(nameof(loss));
            }

            if (Volatile.Read(ref closed) != 0)
            {
                return new ValueTask<PresentationLossResult>(PresentationLossResult.RuntimeClosed);
            }

            PresentationLossMailboxAdmission admission = mailbox.Write(new PresentationLossMailboxRequest(loss));
            return admission.Accepted
                ? new ValueTask<PresentationLossResult>(WaitForLossResultAsync(admission.Completion, cancellationToken))
                : new ValueTask<PresentationLossResult>(PresentationLossResult.RuntimeClosed);
        }

        public ValueTask<HostIncidentReportResult> ReportAsync (NavigationHostIncident incident, CancellationToken cancellationToken = default)
        {
            if (incident is null)
            {
                throw new ArgumentNullException(nameof(incident));
            }

            // Admission and enqueueing are serialized so a matching Recovery cannot clear an
            // incident while this report is already accepted but has not reached the pump yet.
            HostIncidentMailboxAdmission admission;
            lock (hostIncidentAdmissionSync)
            {
                HostIncidentMailboxRequest request = new(incident)
                {
                    AdmissionOrder = hostIncidentAdmissionOrder + 1,
                };
                if (!IsExactHostIncidentDuplicate(Current, incident))
                {
                    request.AdmissionWatermark = hostIncidentAdmissionWatermark + 1;
                }

                admission = mailbox.Write(request);
                if (admission.Accepted)
                {
                    hostIncidentAdmissionOrder = request.AdmissionOrder;
                    hostIncidentAdmissionWatermark = Math.Max(hostIncidentAdmissionWatermark, request.AdmissionWatermark);
                    acceptedHostIncidentReports.Add(request);
                }
            }
            if (!admission.Accepted)
            {
                return new ValueTask<HostIncidentReportResult>(HostIncidentReportResult.RuntimeClosed);
            }

            return new ValueTask<HostIncidentReportResult>(WaitForHostIncidentResultAsync(admission.Completion, cancellationToken));
        }

        NavigationOperation ISourceNavigationRequestPort.Request (NavigationRequest request) => Submit(request);

        void ISourceNavigationRequestPort.Post (NavigationEntryId entry, PresentationId presentation, Func<NavigationOperation> request)
        {
            if (realizer is not ScreenRuntime screens || screens.Find(Current.GetPresentation(entry)) is not ScreenInstance screen || screen.Id != presentation)
            {
                throw new InvalidOperationException("The screen that posts navigation is no longer available.");
            }
            screen.PostNavigation(() => TrackOperation(ObservePostedAsync(request(), screens)));
        }

        private static async Task ObservePostedAsync (NavigationOperation operation, ScreenRuntime screens)
        {
            try
            {
                NavigationResult result = await operation.WaitForRuntimeAsync();
                if (!result.DestinationCommitted)
                {
                    throw new InvalidOperationException("Posted navigation was " + result.Kind + ".");
                }
            }
            catch (Exception exception)
            {
                screens.ReportEquipmentFailure("Posted navigation failed: " + exception.Message);
            }
        }

        private NavigationOperation Submit (NavigationRequest request)
        {
            if (NavigationCallbackScope.IsExecuting)
            {
                throw new InvalidOperationException("Navigation cannot run inside a lifecycle callback. Register owned work to start after navigation settles.");
            }
            NavigationOperation operation = new();
            request = request with
            {
                Operation = operation,
                CancellationToken = operation.CancellationToken
            };
            NavigationOutcome? rejected = Admit(ref request, out ReservationCoordinator.ReservationLease? reservation);
            ValueTask<NavigationOutcome> pending = rejected is null ? Enqueue(request, reservation!) : new ValueTask<NavigationOutcome>(rejected);
            TrackOperation(operation.ObserveAsync(CompleteOperationAsync(pending, reservation, operation.CancellationToken)));
            return operation;
        }

        private async ValueTask<NavigationResult> CompleteOperationAsync (ValueTask<NavigationOutcome> pending, ReservationCoordinator.ReservationLease? reservation, CancellationToken cancellationToken)
        {
            NavigationOutcome result;
            try
            {
                result = await pending;
            }
            finally
            {
                reservation?.Dispose();
            }
            // Even a rejected request returns its handle before invoking an observer.
            await Task.Yield();
            return await ObserveCompletionAsync(new ValueTask<NavigationOutcome>(result), cancellationToken);
        }

        private async ValueTask<NavigationResult> ObserveCompletionAsync (ValueTask<NavigationOutcome> pending, CancellationToken cancellationToken = default)
        {
            NavigationOutcome result = await pending;
            if (realizer is ScreenRuntime screens)
            {
                await screens.FlushCommitNotificationsAsync();
                result = screens.CompleteResult(result);
            }

            NavigationResult completed;
            try
            {
                completed = result.GetResult(cancellationToken);
            }
            catch (Exception exception)
            {
                calls?.FailReturning(exception, result.Changes.RemovedEntries.Where(entry => entry.CallId.HasValue).Select(entry => entry.CallId!.Value).ToArray());
                throw;
            }
            if (operationCompleted is not null)
            {
                try
                {
                    await operationCompleted(completed);
                }
                catch (Exception exception)
                {
                    result = result with
                    {
                        Diagnostics = result.Diagnostics.Concat(new[] { new NavigationDiagnostic(NavigationPhase.Complete, "The operation-completed observer failed: " + exception.Message) { Exception = exception } }).ToArray()
                    };
                }
            }

            // Deferred screen work may immediately start another operation. Complete the
            // current operation's notifications before opening that next execution turn.
            if (realizer is ScreenRuntime settledScreens)
            {
                await calls!.SettleAsync();
                await settledScreens.MarkSettledAsync();
                settledScreens.ScheduleWork();
            }

            return result.GetResult(cancellationToken);
        }

        private ValueTask<NavigationOutcome> Enqueue (NavigationRequest request, ReservationCoordinator.ReservationLease reservation) => mailbox.Write(new NavigationMailboxRequest(request, reservation), Current);

        private NavigationOutcome? Admit (ref NavigationRequest request, out ReservationCoordinator.ReservationLease? reservation)
        {
            reservation = null;
            NavigationOperationId operationId = request.Operation?.Id ?? new NavigationOperationId(Guid.NewGuid());
            NavigationState snapshot = Current;
            if (Volatile.Read(ref closed) != 0)
            {
                throw new NavigationRuntimeClosedException();
            }

            if (request.Source is not null && !request.Source.IsCurrent(snapshot))
            {
                return Result(request.Kind, NavigationOutcomeKind.Rejected, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The source presentation is no longer current.", operationId);
            }

            // A current host incident is an admission barrier. Do this before planning or
            // reservation so a rejected request cannot acquire logical or physical resources.
            if (snapshot.HostIncident is not null)
            {
                return Result(request.Kind, NavigationOutcomeKind.Rejected, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The presentation host requires recovery before accepting navigation.", operationId);
            }

            NavigationPlan plan;
            try
            {
                if (request.ReplacementTarget is not null)
                {
                    request = request with
                    {
                        Replacement = NavigationPlanner.ResolveReplacement(request.ReplacementTarget, snapshot, request.Target)
                    };
                }
                plan = planner.Plan(snapshot, request.Kind, request.Target, request.Destination, request.CallChange, request.Options, request.Replacement?.EntryId, request.ReplacementTarget?.IsCurrent == false);
            }
            catch (NavigationRejectionException exception)
            {
                return Result(request.Kind, NavigationOutcomeKind.Rejected, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId);
            }
            catch (NavigationConflictException exception)
            {
                return Result(request.Kind, NavigationOutcomeKind.Conflict, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId);
            }

            IReadOnlyCollection<RegionInstanceId> reads = request.Source is null ? plan.ReadRegions : GatherSourceReadRegions(snapshot, plan.ReadRegions, request.Source);
            if (request.Operation is not null)
            {
                request.Operation.RemovedEntries = plan.Changes.RemovedEntries.Select(entry => entry.Id).ToArray();
            }
            IReadOnlyCollection<RegionInstanceId> writes = plan.WriteRegions;
            if (realizer is ScreenRuntime screens)
            {
                PresentationTransition transition = new(operationId, request.Kind, snapshot, plan.ProposedAfter, CompositionDeriver.Derive(definition, snapshot), CompositionDeriver.Derive(definition, plan.ProposedAfter), CollectDepartureCandidates(snapshot, plan.ProposedAfter), targetRegion: request.Target, options: request.Options);
                if (screens.RequiresHostReservation(transition))
                {
                    writes = snapshot.Regions.Keys.ToArray();
                }
            }
            if (!reservations.TryAcquire(reads, writes, out reservation))
            {
                return Result(request.Kind, NavigationOutcomeKind.Conflict, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The requested histories are reserved by another operation.", operationId);
            }

            return null;
        }

        private async Task PumpMailboxAsync ()
        {
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    NavigationMailboxItem? item = await mailbox.ReadAsync(shutdown.Token);
                    if (item is null)
                    {
                        continue;
                    }

                    await notificationBarrier.WaitAsync();
                    if (Volatile.Read(ref closed) != 0)
                    {
                        CompleteClosedMailboxItem(item, Current);
                        continue;
                    }

                    switch (item)
                    {
                        case NavigationMailboxRequest request:
                            TrackOperation(CompleteNavigationRequestAsync(request));
                            break;
                        case PresentationLossMailboxRequest loss:
                            TrackOperation(CompleteLossRequestAsync(loss));
                            break;
                        case NavigationRecoveryMailboxRequest recovery:
                            TrackOperation(CompleteRecoveryRequestAsync(recovery));
                            break;
                        case HostIncidentMailboxRequest incident:
                            await CompleteHostIncidentRequestAsync(incident);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
        }

        private async Task CompleteNavigationRequestAsync (NavigationMailboxRequest request)
        {
            try
            {
                if (Volatile.Read(ref closed) != 0)
                {
                    request.Completion.TrySetResult(request.Closed(Current));
                    return;
                }

                NavigationOutcome result = await ExecuteAsync(new NavigationOperationContext(request.Request.Operation!.Id, request.Request), request.Reservation);
                request.Completion.TrySetResult(result);
            }
            catch (Exception exception)
            {
                request.Completion.TrySetResult(Result(request.Request.Kind, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, error: exception));
            }
        }

        private async Task CompleteLossRequestAsync (PresentationLossMailboxRequest request)
        {
            try
            {
                if (Volatile.Read(ref closed) != 0)
                {
                    request.Completion.TrySetResult(PresentationLossResult.RuntimeClosed);
                    return;
                }

                request.Completion.TrySetResult(await ReportLossCoreAsync(request.Loss));
            }
            catch (OperationCanceledException) when (Volatile.Read(ref closed) != 0)
            {
                request.Completion.TrySetResult(PresentationLossResult.RuntimeClosed);
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
            }
        }

        private async Task CompleteHostIncidentRequestAsync (HostIncidentMailboxRequest request)
        {
            try
            {
                if (Volatile.Read(ref closed) != 0)
                {
                    request.Completion.TrySetResult(HostIncidentReportResult.RuntimeClosed);
                    return;
                }

                request.Completion.TrySetResult(await ReportHostIncidentCoreAsync(request));
            }
            catch (OperationCanceledException) when (Volatile.Read(ref closed) != 0)
            {
                request.Completion.TrySetResult(HostIncidentReportResult.RuntimeClosed);
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
            }
            finally
            {
                MarkHostIncidentProcessed(request.AdmissionWatermark);
                lock (hostIncidentAdmissionSync)
                {
                    acceptedHostIncidentReports.Remove(request);
                }
            }
        }

        private void MarkHostIncidentProcessed (long admissionWatermark)
        {
            if (admissionWatermark == 0)
            {
                return;
            }

            lock (hostIncidentAdmissionSync)
            {
                hostIncidentProcessedWatermark = Math.Max(hostIncidentProcessedWatermark, admissionWatermark);
            }
        }

        private async Task CompleteRecoveryRequestAsync (NavigationRecoveryMailboxRequest request)
        {
            try
            {
                if (Volatile.Read(ref closed) != 0)
                {
                    request.Completion.TrySetResult(request.Closed(Current));
                    return;
                }

                NavigationState snapshot = Current;
                bool isCurrentHostRecovery = snapshot.HostIncident is not null && snapshot.HostIncident.Id.Equals(request.IncidentId);
                bool isCurrentPresentationLossRecovery = snapshot.Presentations.Values.Any(presentation => presentation.IncidentId.HasValue && presentation.IncidentId.Value.Equals(request.IncidentId));
                if (snapshot.HostIncident is not null && !isCurrentHostRecovery && !isCurrentPresentationLossRecovery)
                {
                    request.Completion.TrySetResult(Result(NavigationOperationKind.Recovery, NavigationOutcomeKind.Rejected, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The presentation host requires its current incident to recover first."));
                    return;
                }

                if (!isCurrentHostRecovery && !isCurrentPresentationLossRecovery)
                {
                    request.Completion.TrySetResult(Result(NavigationOperationKind.Recovery, NavigationOutcomeKind.Rejected, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The loss incident is no longer current."));
                    return;
                }

                if (!TryReserveRecovery(request.IncidentId))
                {
                    request.Completion.TrySetResult(Result(NavigationOperationKind.Recovery, NavigationOutcomeKind.Conflict, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, "A recovery attempt for this incident is already in progress."));
                    return;
                }

                try
                {
                    request.Completion.TrySetResult(await StartRecoveryAsync(request.IncidentId, request.CancellationToken));
                }
                finally
                {
                    ReleaseRecovery(request.IncidentId);
                }
            }
            catch (Exception exception)
            {
                request.Completion.TrySetResult(Result(NavigationOperationKind.Recovery, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.Failed, exception.Message, error: exception));
            }
        }

        private void CompleteClosedMailboxItem (NavigationMailboxItem item, NavigationState finalSnapshot)
        {
            switch (item)
            {
                case NavigationMailboxRequest navigation:
                    navigation.Completion.TrySetResult(navigation.Closed(finalSnapshot));
                    break;
                case NavigationRecoveryMailboxRequest recovery:
                    recovery.Completion.TrySetResult(recovery.Closed(finalSnapshot));
                    break;
                case PresentationLossMailboxRequest loss:
                    loss.Completion.TrySetResult(PresentationLossResult.RuntimeClosed);
                    break;
                case HostIncidentMailboxRequest incident:
                    incident.Completion.TrySetResult(HostIncidentReportResult.RuntimeClosed);
                    break;
            }
        }

        private void TrackOperation (Task operation)
        {
            lock (operationSync)
            {
                activeOperations.Add(operation);
            }

            _ = operation.ContinueWith(completed =>
            {
                lock (operationSync)
                {
                    activeOperations.Remove(completed);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task WaitForActiveOperationsAsync ()
        {
            while (true)
            {
                Task[] pending;
                lock (operationSync)
                {
                    if (activeOperations.Count == 0)
                    {
                        return;
                    }

                    pending = activeOperations.ToArray();
                }

                await Task.WhenAll(pending.Select(AwaitIgnoringFailureAsync));
            }
        }

        private static async Task AwaitIgnoringFailureAsync (Task operation)
        {
            try
            {
                await operation;
            }
            catch (Exception)
            {
            }
        }

        private async Task<NavigationOutcome> ExecuteAsync (NavigationOperationContext context, ReservationCoordinator.ReservationLease reservation)
        {
            NavigationOperationId operationId = context.OperationId;
            NavigationRequest request = context.Request;
            NavigationOperationKind kind = request.Kind;
            RegionInstanceId target = request.Target;
            INavigationDestinationTree? destination = request.Destination;
            CancellationToken cancellationToken = request.CancellationToken;
            SourcePrecondition? source = request.Source;
            OperationDiagnostics diagnostics = context.Diagnostics;
            RuntimeProgressReporter progress = context.Progress;
            try
            {
                NavigationState before = Current;
                if (before.HostIncident is not null)
                {
                    return Result(kind, NavigationOutcomeKind.Rejected, false, before, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The presentation host requires recovery before accepting navigation.", operationId);
                }

                NavigationState initial = before;
                if (source is not null && !source.IsCurrent(before))
                {
                    return Result(kind, NavigationOutcomeKind.Rejected, false, before, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The source presentation is no longer current.", operationId);
                }

                if (request.Replacement is not null && !request.Replacement.IsCurrent(before))
                {
                    return Result(kind, NavigationOutcomeKind.Conflict, false, before, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The selected replacement history or its presentations changed after acceptance.", operationId);
                }

                NavigationPlan plan = planner.Plan(before, kind, target, destination, request.CallChange, request.Options, request.Replacement?.EntryId, request.ReplacementTarget?.IsCurrent == false);
                PresentationTransition transition = new(operationId, kind, before, plan.ProposedAfter, CompositionDeriver.Derive(definition, before), CompositionDeriver.Derive(definition, plan.ProposedAfter), CollectDepartureCandidates(before, plan.ProposedAfter), targetRegion: target, options: request.Options)
                {
                    ActiveOperation = request.Operation,
                    CallChange = request.CallChange,
                    WaitForTermination = request.ReplacementTarget?.IsCurrent == false || request.Options?.RecreateInstance == true
                };
                await using IPresentationTransaction transaction = realizer.Begin(transition, progress);
                ValidateDepartures(transaction.DepartureEntries, transition.AllowedDepartureEntries);

                if (transaction.DepartureEntries.Count > 0)
                {
                    NavigationState departure = ApplyDeparture(before, transaction.DepartureEntries);
                    NavigationOutcome departureResult = await ExecutePublicationAsync(operationId, kind, NavigationPhase.Departure, transaction, updates.Create(PresentationUpdateKind.Departure, before, departure), NavigationDelta.Empty, null, null, diagnostics, cancellationToken, false, operation: request.Operation);
                    bool departureCompletionFailed = departureResult.Kind == NavigationOutcomeKind.CommittedWithFault
                        && HasCompletionFailure(departureResult);
                    if (departureResult.Kind == NavigationOutcomeKind.Committed
                        || (departureResult.Kind == NavigationOutcomeKind.CommittedWithFault && !departureCompletionFailed))
                    {
                        // The departure crossed the Core Apply boundary. A destination failure
                        // must restore that checkpoint; a departure rejected before Apply has no
                        // physical state that this operation owns.
                    }
                    else if (departureCompletionFailed)
                    {
                        NavigationOutcome restored = await RestoreAfterDepartureAsync(operationId, kind, transaction, initial, departureResult);
                        return restored.Restoration == RestorationOutcome.Restored
                            ? restored with
                            {
                                Kind = NavigationOutcomeKind.Faulted,
                                DestinationCommitted = false
                            }
                            : restored;
                    }
                    else
                    {
                        return departureResult;
                    }

                    before = Current;
                    try
                    {
                        plan = planner.Plan(before, kind, target, destination, request.CallChange, request.Options, request.Replacement?.EntryId, request.ReplacementTarget?.IsCurrent == false);
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                    {
                        NavigationOutcome replanningFailure = Result(kind, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId, error: exception);
                        NavigationOutcome restored = await RestoreAfterDepartureAsync(operationId, kind, transaction, initial, replanningFailure);
                        return restored.Restoration == RestorationOutcome.Restored
                            ? restored with
                            {
                                Kind = NavigationOutcomeKind.Faulted,
                                DestinationCommitted = false
                            }
                            : restored;
                    }
                }

                progress.Report(NavigationPhase.Prepare, null);
                NavigationOutcome destinationResult;
                try
                {
                    destinationResult = await ExecutePublicationAsync(operationId, kind, NavigationPhase.Commit, transaction, updates.Create(PresentationUpdateKind.Destination, before, plan.ProposedAfter, plan.WriteRegions), plan.Changes, plan, transaction.DepartureEntries.Count == 0 ? source : null, diagnostics, cancellationToken, true, operation: request.Operation);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || request.Operation?.CancellationRequested == true)
                {
                    destinationResult = Result(kind, NavigationOutcomeKind.Cancelled, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The navigation request was cancelled before destination commit.", operationId);
                }
                catch (Exception exception)
                {
                    // Preparation can fail before ExecutePublicationAsync has a prepared
                    // publication to classify. Keep that failure in the outgoing-first
                    // matrix so an already-applied departure still gets its restoration
                    // attempt.
                    destinationResult = Result(kind, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId, error: exception);
                }

                if (transaction.DepartureEntries.Count > 0
                    && destinationResult.Kind != NavigationOutcomeKind.Committed
                    && destinationResult.Kind != NavigationOutcomeKind.CommittedWithFault)
                {
                    NavigationOutcome restored = await RestoreAfterDepartureAsync(operationId, kind, transaction, initial, destinationResult);
                    return restored.Restoration == RestorationOutcome.Restored
                        ? restored with
                        {
                            Kind = destinationResult.Kind,
                            DestinationCommitted = false
                        }
                        : restored;
                }

                return destinationResult;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || request.Operation?.CancellationRequested == true)
            {
                return Result(kind, NavigationOutcomeKind.Cancelled, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The navigation request was cancelled before destination commit.", operationId);
            }
            catch (NavigationConflictException exception)
            {
                return Result(kind, NavigationOutcomeKind.Conflict, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId);
            }
            catch (Exception exception)
            {
                return Result(kind, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId, error: exception);
            }
            finally
            {
                reservation.Dispose();
            }
        }

        private async Task<NavigationOutcome> ExecutePublicationAsync (NavigationOperationId operationId, NavigationOperationKind kind, NavigationPhase phase, IPresentationTransaction transaction, PresentationUpdate update, NavigationDelta changes, NavigationPlan? plan, SourcePrecondition? source, OperationDiagnostics diagnostics, CancellationToken cancellationToken, bool destination, long recoveryAdmissionWatermark = -1, PresentationRecoveryContext? recovery = null, IReadOnlyList<PresentationReference>? recoveryRequiredPresentations = null, NavigationOperation? operation = null)
        {
            NavigationHostIncident? incidentBeforePrepare = Current.HostIncident;
            if (incidentBeforePrepare is not null && kind != NavigationOperationKind.Recovery)
            {
                return Result(kind, NavigationOutcomeKind.Rejected, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The presentation host requires recovery before accepting navigation.", operationId, phase);
            }

            using CancellationTokenSource preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
            await using IPreparedPublication publication = await transaction.PrepareAsync(update, preparationCancellation.Token);
            PublicationOutcome outcome;
            NavigationState published;
            await commitGate.WaitAsync(shutdown.Token);
            notificationBarrier.Enter();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                NavigationState latest = Current;
                if (latest.HostIncident is not null && kind != NavigationOperationKind.Recovery)
                {
                    NavigationHostIncident incident = latest.HostIncident;
                    if (incidentBeforePrepare is null || !incidentBeforePrepare.Id.Equals(incident.Id))
                    {
                        return Result(kind, NavigationOutcomeKind.Faulted, false, latest, NavigationDelta.Empty, RestorationOutcome.Failed, incident.Reason, operationId, phase);
                    }

                    return Result(kind, NavigationOutcomeKind.Rejected, false, latest, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The presentation host requires recovery before accepting navigation.", operationId, phase);
                }

                if (source is not null && !source.IsCurrent(latest))
                {
                    return Result(kind, NavigationOutcomeKind.Conflict, false, latest, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The source presentation changed before commit.", operationId);
                }

                NavigationState merged = latest;
                if (plan is not null && !plan.TryMerge(latest, out merged))
                {
                    return Result(kind, NavigationOutcomeKind.Conflict, false, latest, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The state changed before commit.", operationId);
                }

                if (plan is not null && !PresentationUpdateScopeValidator.IsCurrent(update, definition, latest))
                {
                    return Result(kind, NavigationOutcomeKind.Conflict, false, latest, NavigationDelta.Empty, RestorationOutcome.NotRequired, "A retained presentation changed before commit.", operationId);
                }

                if (plan is null && latest.Revision != update.Before.Revision)
                {
                    return Result(kind, NavigationOutcomeKind.Conflict, false, latest, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The state changed before commit.", operationId);
                }

                NavigationState candidate = plan is null
                    ? update.ProposedAfter.With(latest.Revision + 1, update.ProposedAfter.Regions.ToDictionary(static item => item.Key, static item => item.Value), update.ProposedAfter.Entries.ToDictionary(static item => item.Key, static item => item.Value), update.ProposedAfter.Presentations.ToDictionary(static item => item.Key, static item => item.Value), update.ProposedAfter.HostIncident)
                    : merged;
                cancellationToken.ThrowIfCancellationRequested();
                NavigationCommitCapability capability = new(state, candidate, CompositionDeriver.Derive(definition, candidate), cancellationToken, operation);
                try
                {
                    outcome = await publication.CommitAsync(capability, shutdown.Token);
                    if (outcome.Disposition == CommitDisposition.HostIncident)
                    {
                        NavigationHostIncident? adapterIncident = outcome.HostIncident;
                        if (adapterIncident is null)
                        {
                            PresentationFailure failure = UnexpectedPublicationFailure(Current, NavigationPhase.Commit, "The presentation adapter returned an invalid host-incident disposition.");
                            published = PublishFailures(Current, new[] { failure });
                            if (capability.IsApplied)
                            {
                                NotifyDestinationObservers(destination, published, operationId, changes, diagnostics);
                                return PublicationResult(operationId, kind, destination, published, changes, new[] { failure }, diagnostics.Snapshot(), true);
                            }

                            return Result(kind, NavigationOutcomeKind.Faulted, false, published, NavigationDelta.Empty, RestorationOutcome.NotRequired, failure.Reason, operationId, NavigationPhase.Commit);
                        }

                        PresentationFailure hostFailure = new(
                            PresentationFailureScope.PresentationHost,
                            NavigationPhase.Commit,
                            adapterIncident.Reason,
                            Array.Empty<PresentationReference>(),
                            null,
                            adapterIncident);
                        NavigationState incidentState = PublishFailures(Current, new[] { hostFailure });
                        if (capability.IsApplied)
                        {
                            NotifyDestinationObservers(destination, incidentState, operationId, changes, diagnostics);
                            return PublicationResult(operationId, kind, destination, incidentState, changes, new[] { hostFailure }, diagnostics.Snapshot(), true);
                        }

                        return Result(kind, NavigationOutcomeKind.Faulted, false, incidentState, NavigationDelta.Empty, RestorationOutcome.NotRequired, adapterIncident.Reason, operationId, NavigationPhase.Commit);
                    }

                    if (outcome.Disposition == CommitDisposition.Conflict)
                    {
                        if (capability.IsApplied)
                        {
                            PresentationFailure failure = UnexpectedPublicationFailure(Current, NavigationPhase.Commit, "The presentation adapter returned Conflict after applying the Core candidate.");
                            published = PublishFailures(Current, new[] { failure });
                            NotifyDestinationObservers(destination, published, operationId, changes, diagnostics);
                            return PublicationResult(operationId, kind, destination, published, changes, new[] { failure }, diagnostics.Snapshot(), true);
                        }

                        return Result(kind, NavigationOutcomeKind.Conflict, false, latest, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The presentation adapter rejected the prepared conditions.", operationId);
                    }

                    if (!capability.IsApplied)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        PresentationFailure failure = UnexpectedPublicationFailure(Current, NavigationPhase.Commit, "The presentation adapter returned Applied without applying the Core candidate.");
                        published = PublishFailures(Current, new[] { failure });
                        return Result(kind, NavigationOutcomeKind.Faulted, false, published, NavigationDelta.Empty, RestorationOutcome.NotRequired, failure.Reason, operationId, NavigationPhase.Commit);
                    }

                    published = PublishFailures(Current, outcome.Failures);
                    NotifyDestinationObservers(destination, published, operationId, changes, diagnostics);
                }
                catch (Exception exception) when (capability.IsApplied)
                {
                    PresentationFailure failure = UnexpectedPublicationFailure(Current, NavigationPhase.Commit, exception.Message, exception);
                    published = PublishFailures(Current, new[] { failure });
                    NotifyDestinationObservers(destination, published, operationId, changes, diagnostics);
                    return PublicationResult(operationId, kind, destination, published, changes, new[] { failure }, diagnostics.Snapshot(), true);
                }
                catch (OperationCanceledException) when ((cancellationToken.IsCancellationRequested || operation?.CancellationRequested == true) && !capability.IsApplied)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Result(kind, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, exception.Message, operationId, NavigationPhase.Commit, error: exception);
                }
                finally
                {
                    capability.Expire();
                }
            }
            finally
            {
                notificationBarrier.Exit();
                commitGate.Release();
                if (realizer is ScreenRuntime screens)
                {
                    await screens.FlushCommitNotificationsAsync();
                }
            }

            // Completion may await animation or resource cleanup; it must not delay later commit observers.
            PresentationCompletion completion;
            try
            {
                completion = await publication.CompleteAsync(shutdown.Token);
            }
            catch (OperationCanceledException exception) when (shutdown.IsCancellationRequested)
            {
                // Apply and observer delivery establish the destination before completion begins.
                // Shutdown may interrupt cleanup, but it cannot make that established destination uncommitted.
                diagnostics.Add(NavigationPhase.Complete, "Presentation completion was interrupted while the runtime was closing: " + exception.Message, exception);
                if (destination)
                {
                    return PublicationResult(operationId, kind, true, published, changes, outcome.Failures, diagnostics.Snapshot(), true);
                }

                PresentationFailure failure = new(
                    PresentationFailureScope.RetiredResources,
                    NavigationPhase.Complete,
                    "Presentation completion was interrupted while the runtime is closing: " + exception.Message,
                    Array.Empty<PresentationReference>());
                return PublicationResult(operationId, kind, false, published, NavigationDelta.Empty, outcome.Failures.Concat(new[] { failure }), diagnostics.Snapshot(), true);
            }
            catch (Exception exception)
            {
                PresentationFailure failure = UnexpectedPublicationFailure(published, NavigationPhase.Complete, exception.Message, exception);
                NavigationState failedCompletion = await ApplyCompletionFailuresAsync(new[] { failure });
                return PublicationResult(operationId, kind, destination, failedCompletion, destination ? changes : NavigationDelta.Empty, outcome.Failures.Concat(new[] { failure }), diagnostics.Snapshot(), true);
            }

            NavigationState final = completion.Failures.Count == 0
                ? published
                : await ApplyCompletionFailuresAsync(completion.Failures);
            bool faults = outcome.Failures.Count > 0 || completion.Failures.Count > 0;
            PresentationFailure[] publicationFailures = outcome.Failures.Concat(completion.Failures).ToArray();
            if (recoveryAdmissionWatermark >= 0
                && recovery?.Kind == PresentationRecoveryKind.HostIncident
                && publicationFailures.All(static failure => failure.Scope == PresentationFailureScope.RetiredResources))
            {
                final = await ClearHostIncidentAfterRecoveryAsync(recovery.IncidentId, recoveryAdmissionWatermark, recoveryRequiredPresentations ?? Array.Empty<PresentationReference>());
            }

            return PublicationResult(operationId, kind, destination, final, changes, publicationFailures, diagnostics.Snapshot(), faults);
        }

        private async ValueTask<NavigationState> ApplyCompletionFailuresAsync (IReadOnlyList<PresentationFailure> failures)
        {
            await commitGate.WaitAsync();
            try
            {
                return PublishFailures(Current, failures);
            }
            finally
            {
                commitGate.Release();
            }
        }

        private async Task<NavigationOutcome> RestoreAfterDepartureAsync (NavigationOperationId operationId, NavigationOperationKind kind, IPresentationTransaction transaction, NavigationState original, NavigationOutcome failedDeparture)
        {
            if (Current.HostIncident is not null)
            {
                return failedDeparture with
                {
                    Kind = NavigationOutcomeKind.Faulted,
                    DestinationCommitted = false,
                    FinalSnapshot = Current,
                    Restoration = RestorationOutcome.Failed,
                    Diagnostics = failedDeparture.Diagnostics.Concat(new[] { new NavigationDiagnostic(NavigationPhase.Restore, "The source departure was followed by a host incident; unrelated restoration was not attempted.") }).ToArray(),
                };
            }

            NavigationState restoreBefore = Current;
            NavigationState restored = CreateRestorationCandidate(restoreBefore, original, transaction.DepartureEntries);
            try
            {
                NavigationOutcome restoration = await ExecutePublicationAsync(operationId, kind, NavigationPhase.Restore, transaction, updates.Create(PresentationUpdateKind.Restoration, restoreBefore, restored), NavigationDelta.Empty, null, null, new OperationDiagnostics(), CancellationToken.None, false);
                if (restoration.Kind == NavigationOutcomeKind.Committed)
                {
                    return restoration with
                    {
                        Kind = failedDeparture.Kind,
                        DestinationCommitted = false,
                        Restoration = RestorationOutcome.Restored,
                        Diagnostics = failedDeparture.Diagnostics.Concat(restoration.Diagnostics).ToArray()
                    };
                }

                NavigationState lost = await MarkLostAsync(transaction.DepartureEntries, "The source could not be restored after departure.");
                return restoration with
                {
                    Kind = NavigationOutcomeKind.Faulted,
                    DestinationCommitted = false,
                    FinalSnapshot = lost,
                    Restoration = RestorationOutcome.Failed,
                    Diagnostics = failedDeparture.Diagnostics.Concat(restoration.Diagnostics).Concat(new[] { new NavigationDiagnostic(NavigationPhase.Restore, "The source could not be restored after departure.") }).ToArray()
                };
            }
            catch (OperationCanceledException exception) when (shutdown.IsCancellationRequested)
            {
                return failedDeparture with
                {
                    Kind = NavigationOutcomeKind.Faulted,
                    DestinationCommitted = false,
                    FinalSnapshot = Current,
                    Restoration = RestorationOutcome.Failed,
                    Diagnostics = failedDeparture.Diagnostics.Concat(new[] { new NavigationDiagnostic(NavigationPhase.Restore, "Restoration was interrupted while the runtime was closing: " + exception.Message) { Exception = exception } }).ToArray(),
                };
            }
            catch (Exception exception)
            {
                NavigationState lost = await MarkLostAsync(transaction.DepartureEntries, exception.Message);
                return failedDeparture with
                {
                    Kind = NavigationOutcomeKind.Faulted,
                    DestinationCommitted = false,
                    FinalSnapshot = lost,
                    Restoration = RestorationOutcome.Failed,
                    Diagnostics = failedDeparture.Diagnostics.Concat(new[] { new NavigationDiagnostic(NavigationPhase.Restore, exception.Message) { Exception = exception } }).ToArray()
                };
            }
        }

        private static NavigationState CreateRestorationCandidate (NavigationState current, NavigationState original, IReadOnlyList<NavigationEntryId> departureEntries)
        {
            Dictionary<NavigationEntryId, PresentationState> presentations = new(current.Presentations);
            foreach (NavigationEntryId entryId in departureEntries)
            {
                if (original.Presentations.TryGetValue(entryId, out PresentationState? originalPresentation) && originalPresentation.Materialization == PresentationMaterialization.Available && presentations.ContainsKey(entryId))
                {
                    presentations[entryId] = PresentationState.Available(entryId);
                }
            }

            return current.With(current.Revision, current.Regions.ToDictionary(static item => item.Key, static item => item.Value), current.Entries.ToDictionary(static item => item.Key, static item => item.Value), presentations, NormalizeHostIncident(current.HostIncident, presentations));
        }

        private async ValueTask<NavigationState> MarkLostAsync (IReadOnlyList<NavigationEntryId> entryIds, string reason)
        {
            await commitGate.WaitAsync(shutdown.Token);
            try
            {
                NavigationState current = Current;
                Dictionary<NavigationEntryId, PresentationState> presentations = new(current.Presentations);
                bool changed = false;
                foreach (NavigationEntryId entryId in entryIds)
                {
                    if (presentations.TryGetValue(entryId, out PresentationState? presentation) && presentation.Materialization != PresentationMaterialization.Lost)
                    {
                        presentations[entryId] = PresentationState.Lost(entryId, presentation.Id, new NavigationIncidentId(Guid.NewGuid()));
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return current;
                }

                NavigationState lost = current.With(current.Revision + 1, current.Regions.ToDictionary(static item => item.Key, static item => item.Value), current.Entries.ToDictionary(static item => item.Key, static item => item.Value), presentations, NormalizeHostIncident(current.HostIncident, presentations));
                state.Publish(lost);
                return lost;
            }
            finally
            {
                commitGate.Release();
            }
        }

        private async ValueTask<NavigationOutcome> StartRecoveryAsync (NavigationIncidentId incidentId, CancellationToken cancellationToken)
        {
            NavigationState before = Current;
            Dictionary<NavigationEntryId, PresentationState> presentations = new(before.Presentations);
            bool hostRecovery = before.HostIncident is not null && before.HostIncident.Id.Equals(incidentId);
            PresentationRecoveryKind recoveryKind = hostRecovery ? PresentationRecoveryKind.HostIncident : PresentationRecoveryKind.PresentationLoss;
            long recoveryAdmissionWatermark = Volatile.Read(ref hostIncidentAdmissionWatermark);
            IReadOnlyList<PresentationReference> required = hostRecovery
                ? NormalizeRequiredPresentations(before, before.HostIncident!.RequiredPresentations, Array.Empty<PresentationReference>())
                : before.Presentations.Values
                    .Where(presentation => presentation.IncidentId.HasValue && presentation.IncidentId.Value.Equals(incidentId) && presentation.Id.HasValue)
                    .Select(presentation => new PresentationReference(presentation.EntryId, presentation.Id!.Value))
                    .ToArray();

            NavigationHostIncident? candidateHostIncident = before.HostIncident;

            if (hostRecovery)
            {
                List<PresentationReference> candidateRequired = new();
                foreach (PresentationReference member in required)
                {
                    if (before.Presentations.TryGetValue(member.EntryId, out PresentationState? presentation)
                        && presentation.Materialization == PresentationMaterialization.Available
                        && presentation.Id.HasValue
                        && presentation.Id.Value.Equals(member.PresentationId))
                    {
                        presentations[member.EntryId] = PresentationState.Available(member.EntryId);
                        candidateRequired.Add(new PresentationReference(member.EntryId, presentations[member.EntryId].Id!.Value));
                    }
                }

                candidateHostIncident = new NavigationHostIncident(before.HostIncident!.Id, before.HostIncident.Reason, candidateRequired);
            }
            else
            {
                foreach (PresentationState presentation in before.Presentations.Values.Where(presentation => presentation.IncidentId.HasValue && presentation.IncidentId.Value.Equals(incidentId)))
                {
                    presentations[presentation.EntryId] = PresentationState.Available(presentation.EntryId);
                }
            }

            candidateHostIncident = NormalizeHostIncident(candidateHostIncident, presentations);
            if (hostRecovery)
            {
                recoveryAdmissionWatermark = BeginHostRecovery(incidentId);
            }

            NavigationState after = before.With(before.Revision, before.Regions.ToDictionary(static item => item.Key, static item => item.Value), before.Entries.ToDictionary(static item => item.Key, static item => item.Value), presentations, candidateHostIncident);
            NavigationOperationId operationId = new(Guid.NewGuid());
            PresentationRecoveryContext recovery = new(recoveryKind, incidentId);
            try
            {
                OperationDiagnostics diagnostics = new();
                await using IPresentationTransaction transaction = realizer.Begin(new PresentationTransition(operationId, NavigationOperationKind.Recovery, before, after, CompositionDeriver.Derive(definition, before), CompositionDeriver.Derive(definition, after), Array.Empty<NavigationEntryId>(), recovery), new RuntimeProgressReporter(operationId, null, diagnostics));
                return await ExecutePublicationAsync(operationId, NavigationOperationKind.Recovery, NavigationPhase.Restore, transaction, updates.Create(PresentationUpdateKind.Restoration, before, after), NavigationDelta.Empty, null, null, diagnostics, cancellationToken, true, recoveryAdmissionWatermark, recovery, candidateHostIncident?.RequiredPresentations);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result(NavigationOperationKind.Recovery, NavigationOutcomeKind.Cancelled, false, Current, NavigationDelta.Empty, RestorationOutcome.NotRequired, "The navigation request was cancelled before destination commit.", operationId);
            }
            catch (Exception exception)
            {
                return Result(NavigationOperationKind.Recovery, NavigationOutcomeKind.Faulted, false, Current, NavigationDelta.Empty, RestorationOutcome.Failed, exception.Message, operationId, error: exception);
            }
            finally
            {
                if (hostRecovery)
                {
                    EndHostRecovery(incidentId);
                }
            }
        }

        private bool TryReserveRecovery (NavigationIncidentId incidentId)
        {
            lock (hostIncidentAdmissionSync)
            {
                return recoveryReservations.Add(incidentId);
            }
        }

        private void ReleaseRecovery (NavigationIncidentId incidentId)
        {
            lock (hostIncidentAdmissionSync)
            {
                recoveryReservations.Remove(incidentId);
            }
        }

        private long BeginHostRecovery (NavigationIncidentId incidentId)
        {
            lock (hostIncidentAdmissionSync)
            {
                hostRecoveryInProgress = incidentId;
                hostRecoveryStartAdmissionOrder = hostIncidentAdmissionOrder;
                return hostIncidentAdmissionWatermark;
            }
        }

        private void EndHostRecovery (NavigationIncidentId incidentId)
        {
            lock (hostIncidentAdmissionSync)
            {
                if (!hostRecoveryInProgress.HasValue || !hostRecoveryInProgress.Value.Equals(incidentId))
                {
                    return;
                }

                hostRecoveryInProgress = null;
                hostRecoveryStartAdmissionOrder = 0;
                foreach (TaskCompletionSource<HostIncidentReportResult> completion in deferredHostIncidentReports.Values)
                {
                    completion.TrySetResult(HostIncidentReportResult.Obsolete);
                }

                deferredHostIncidentReports.Clear();
            }
        }

        private bool ShouldDeferForHostRecovery (NavigationIncidentId currentIncidentId, long admissionOrder)
        {
            lock (hostIncidentAdmissionSync)
            {
                return hostRecoveryInProgress.HasValue
                    && hostRecoveryInProgress.Value.Equals(currentIncidentId)
                    && admissionOrder > hostRecoveryStartAdmissionOrder;
            }
        }

        private Task<HostIncidentReportResult> DeferHostIncidentReport (HostIncidentMailboxRequest request)
        {
            lock (hostIncidentAdmissionSync)
            {
                TaskCompletionSource<HostIncidentReportResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                deferredHostIncidentReports[request] = completion;
                return completion.Task;
            }
        }

        private static async Task<PresentationLossResult> WaitForLossResultAsync (Task<PresentationLossResult> completion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.CanBeCanceled || completion.IsCompleted)
            {
                return await completion;
            }

            TaskCompletionSource<bool> cancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellation);
            if (completion.IsCompleted)
            {
                return await completion;
            }

            if (await Task.WhenAny(completion, cancellation.Task) == completion)
            {
                return await completion;
            }

            throw new OperationCanceledException(cancellationToken);
        }

        private static async Task<HostIncidentReportResult> WaitForHostIncidentResultAsync (Task<HostIncidentReportResult> completion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.CanBeCanceled || completion.IsCompleted)
            {
                return await completion;
            }

            TaskCompletionSource<bool> cancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancellation);
            if (completion.IsCompleted)
            {
                return await completion;
            }

            if (await Task.WhenAny(completion, cancellation.Task) == completion)
            {
                return await completion;
            }

            throw new OperationCanceledException(cancellationToken);
        }

        private async ValueTask<PresentationLossResult> ReportLossCoreAsync (PresentationLoss loss)
        {
            if (Volatile.Read(ref closed) != 0)
            {
                return PresentationLossResult.RuntimeClosed;
            }

            await commitGate.WaitAsync(shutdown.Token);
            try
            {
                if (Volatile.Read(ref closed) != 0)
                {
                    return PresentationLossResult.RuntimeClosed;
                }

                NavigationState current = Current;
                List<PresentationReference> applicableMembers = new();
                foreach (PresentationReference member in loss.Members)
                {
                    if (current.Presentations.TryGetValue(member.EntryId, out PresentationState? presentation) && presentation.Materialization == PresentationMaterialization.Available && presentation.Id == member.PresentationId)
                    {
                        applicableMembers.Add(member);
                    }
                }

                if (applicableMembers.Count == 0)
                {
                    return PresentationLossResult.Obsolete;
                }

                NavigationIncidentId incident = new(Guid.NewGuid());
                Dictionary<NavigationEntryId, PresentationState> presentations = new(current.Presentations);
                foreach (PresentationReference member in applicableMembers)
                {
                    presentations[member.EntryId] = PresentationState.Lost(member.EntryId, member.PresentationId, incident);
                }

                state.Publish(current.With(current.Revision + 1, current.Regions.ToDictionary(static item => item.Key, static item => item.Value), current.Entries.ToDictionary(static item => item.Key, static item => item.Value), presentations, NormalizeHostIncident(current.HostIncident, presentations)));
                return PresentationLossResult.Applied;
            }
            finally
            {
                commitGate.Release();
            }
        }

        private async ValueTask<HostIncidentReportResult> ReportHostIncidentCoreAsync (HostIncidentMailboxRequest request)
        {
            if (Volatile.Read(ref closed) != 0)
            {
                return HostIncidentReportResult.RuntimeClosed;
            }

            Task<HostIncidentReportResult>? deferred = null;
            await commitGate.WaitAsync(shutdown.Token);
            try
            {
                if (Volatile.Read(ref closed) != 0)
                {
                    return HostIncidentReportResult.RuntimeClosed;
                }

                NavigationHostIncident incident = request.Incident;
                if (resolvedHostIncidents.Contains(incident.Id))
                {
                    return HostIncidentReportResult.Obsolete;
                }

                NavigationState current = Current;
                if (current.HostIncident is not null && !current.HostIncident.Id.Equals(incident.Id))
                {
                    if (ShouldDeferForHostRecovery(current.HostIncident.Id, request.AdmissionOrder))
                    {
                        deferred = DeferHostIncidentReport(request);
                    }
                    else
                    {
                        return HostIncidentReportResult.Obsolete;
                    }
                }
                else
                {
                    PresentationReference[] required = NormalizeRequiredPresentations(current, current.HostIncident?.RequiredPresentations ?? Array.Empty<PresentationReference>(), incident.RequiredPresentations);
                    if (current.HostIncident is null)
                    {
                        NavigationHostIncident applied = new(incident.Id, incident.Reason, required);
                        state.Publish(CreateStateWithHostIncident(current, applied));
                        return HostIncidentReportResult.Applied;
                    }

                    NavigationHostIncident merged = new(current.HostIncident.Id, current.HostIncident.Reason, required);
                    if (current.HostIncident.RequiredPresentations.Count == required.Length
                        && current.HostIncident.RequiredPresentations.SequenceEqual(required))
                    {
                        return HostIncidentReportResult.AlreadyCurrent;
                    }

                    state.Publish(CreateStateWithHostIncident(current, merged));
                    return HostIncidentReportResult.AlreadyCurrent;
                }
            }
            finally
            {
                commitGate.Release();
            }

            return await deferred!;
        }

        private static PresentationReference[] NormalizeRequiredPresentations (NavigationState state, IReadOnlyList<PresentationReference> current, IReadOnlyList<PresentationReference> incoming)
        {
            Dictionary<NavigationEntryId, PresentationReference> members = new();
            HashSet<NavigationEntryId> currentEntries = new(current.Select(static member => member.EntryId));
            foreach (PresentationReference member in current)
            {
                if (IsCurrentHostRecoveryPair(state, member))
                {
                    members[member.EntryId] = member;
                }
            }

            foreach (PresentationReference member in incoming)
            {
                if (!currentEntries.Contains(member.EntryId) && IsCurrentHostRecoveryPair(state, member))
                {
                    members.TryAdd(member.EntryId, member);
                }
            }

            Dictionary<PresentationId, PresentationReference> uniquePresentations = new();
            foreach (PresentationReference member in members.Values)
            {
                uniquePresentations.TryAdd(member.PresentationId, member);
            }

            return uniquePresentations.Values.ToArray();
        }

        private static bool IsCurrentHostRecoveryPair (NavigationState state, PresentationReference member) => state.Presentations.TryGetValue(member.EntryId, out PresentationState? presentation)
            && presentation.Materialization == PresentationMaterialization.Available
            && presentation.Id.HasValue
            && presentation.Id.Value.Equals(member.PresentationId);

        private static NavigationHostIncident? NormalizeHostIncident (NavigationHostIncident? incident, IReadOnlyDictionary<NavigationEntryId, PresentationState> presentations)
        {
            if (incident is null)
            {
                return null;
            }

            PresentationReference[] required = incident.RequiredPresentations
                .Where(member => IsCurrentHostRecoveryPair(presentations, member))
                .ToArray();
            return new NavigationHostIncident(incident.Id, incident.Reason, required);
        }

        private static bool IsCurrentHostRecoveryPair (IReadOnlyDictionary<NavigationEntryId, PresentationState> presentations, PresentationReference member) => presentations.TryGetValue(member.EntryId, out PresentationState? presentation)
            && presentation.Materialization == PresentationMaterialization.Available
            && presentation.Id.HasValue
            && presentation.Id.Value.Equals(member.PresentationId);

        private static bool IsExactHostIncidentDuplicate (NavigationState state, NavigationHostIncident incident)
        {
            if (state.HostIncident is null || !state.HostIncident.Id.Equals(incident.Id))
            {
                return false;
            }

            PresentationReference[] normalized = NormalizeRequiredPresentations(state, state.HostIncident.RequiredPresentations, incident.RequiredPresentations);
            return state.HostIncident.RequiredPresentations.Count == normalized.Length
                && state.HostIncident.RequiredPresentations.SequenceEqual(normalized);
        }

        private static IReadOnlyList<PresentationReference> BuildFallbackRequiredPresentations (NavigationState state) => state.Presentations.Values
            .Where(static presentation => presentation.Materialization == PresentationMaterialization.Available && presentation.Id.HasValue)
            .Select(static presentation => new PresentationReference(presentation.EntryId, presentation.Id!.Value))
            .ToArray();

        private static NavigationState CreateStateWithHostIncident (NavigationState current, NavigationHostIncident incident) => current.With(
            current.Revision + 1,
            current.Regions.ToDictionary(static item => item.Key, static item => item.Value),
            current.Entries.ToDictionary(static item => item.Key, static item => item.Value),
            current.Presentations.ToDictionary(static item => item.Key, static item => item.Value),
            NormalizeHostIncident(incident, current.Presentations));

        private async ValueTask<NavigationState> ClearHostIncidentAfterRecoveryAsync (NavigationIncidentId incidentId, long recoveryAdmissionWatermark, IReadOnlyList<PresentationReference> recoveryRequiredPresentations)
        {
            await commitGate.WaitAsync(shutdown.Token);
            try
            {
                lock (hostIncidentAdmissionSync)
                {
                    NavigationState current = Current;
                    if (current.HostIncident is null
                        || !current.HostIncident.Id.Equals(incidentId))
                    {
                        return current;
                    }

                    bool candidateRequirementsCurrent = current.HostIncident.RequiredPresentations.Count == recoveryRequiredPresentations.Count
                        && current.HostIncident.RequiredPresentations.SequenceEqual(recoveryRequiredPresentations);
                    HostIncidentMailboxRequest[] admitted = acceptedHostIncidentReports
                        .Where(request => request.AdmissionOrder > hostRecoveryStartAdmissionOrder)
                        .OrderBy(request => request.AdmissionOrder)
                        .ToArray();
                    if (!candidateRequirementsCurrent)
                    {
                        return current;
                    }

                    HostIncidentMailboxRequest[] meaningful = admitted
                        .Where(request => request.AdmissionWatermark > recoveryAdmissionWatermark || !request.Incident.Id.Equals(current.HostIncident.Id))
                        .ToArray();
                    HostIncidentMailboxRequest? firstMeaningful = meaningful.FirstOrDefault();
                    if (firstMeaningful is not null && firstMeaningful.Incident.Id.Equals(current.HostIncident.Id))
                    {
                        return current;
                    }

                    HostIncidentMailboxRequest[] deferred = firstMeaningful is null
                        ? Array.Empty<HostIncidentMailboxRequest>()
                        : meaningful.Where(request => request.Incident.Id.Equals(firstMeaningful.Incident.Id)).ToArray();

                    foreach (HostIncidentMailboxRequest duplicate in admitted.Where(request => request.Incident.Id.Equals(current.HostIncident.Id)))
                    {
                        HostIncidentReportResult result = firstMeaningful is not null
                            && !firstMeaningful.Incident.Id.Equals(current.HostIncident.Id)
                            && duplicate.AdmissionOrder > firstMeaningful.AdmissionOrder
                            ? HostIncidentReportResult.Obsolete
                            : HostIncidentReportResult.AlreadyCurrent;
                        duplicate.Completion.TrySetResult(result);
                    }

                    if (deferred.Length > 0)
                    {
                        HostIncidentMailboxRequest handoffRequest = deferred[0];
                        List<(HostIncidentMailboxRequest Request, HostIncidentReportResult Result)> decisions = new();
                        IReadOnlyList<PresentationReference> required = Array.Empty<PresentationReference>();
                        foreach (HostIncidentMailboxRequest deferredRequest in deferred)
                        {
                            if (!deferredRequest.Incident.Id.Equals(handoffRequest.Incident.Id))
                            {
                                decisions.Add((deferredRequest, HostIncidentReportResult.Obsolete));
                                continue;
                            }

                            PresentationReference[] next = NormalizeRequiredPresentations(current, required, deferredRequest.Incident.RequiredPresentations);
                            HostIncidentReportResult result = required.Count == next.Length && required.SequenceEqual(next)
                                ? HostIncidentReportResult.AlreadyCurrent
                                : HostIncidentReportResult.Applied;
                            decisions.Add((deferredRequest, result));
                            required = next;
                        }

                        NavigationHostIncident handoff = new(handoffRequest.Incident.Id, handoffRequest.Incident.Reason, required);
                        NavigationState handedOff = CreateStateWithHostIncident(current, handoff);
                        resolvedHostIncidents.Add(current.HostIncident.Id);
                        state.Publish(handedOff);
                        foreach ((HostIncidentMailboxRequest Request, HostIncidentReportResult Result) decision in decisions)
                        {
                            decision.Request.Completion.TrySetResult(decision.Result);
                            if (deferredHostIncidentReports.TryGetValue(decision.Request, out TaskCompletionSource<HostIncidentReportResult>? completion))
                            {
                                completion.TrySetResult(decision.Result);
                            }
                        }

                        deferredHostIncidentReports.Clear();
                        return handedOff;
                    }

                    if (hostIncidentAdmissionWatermark != recoveryAdmissionWatermark
                        || hostIncidentProcessedWatermark != recoveryAdmissionWatermark)
                    {
                        return current;
                    }

                    NavigationState cleared = current.With(
                        current.Revision + 1,
                        current.Regions.ToDictionary(static item => item.Key, static item => item.Value),
                        current.Entries.ToDictionary(static item => item.Key, static item => item.Value),
                        current.Presentations.ToDictionary(static item => item.Key, static item => item.Value),
                        null);
                    resolvedHostIncidents.Add(incidentId);
                    state.Publish(cleared);
                    return cleared;
                }
            }
            finally
            {
                commitGate.Release();
            }
        }

        public async ValueTask DisposeAsync ()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
            {
                return;
            }

            NavigationState finalSnapshot = Current;
            IReadOnlyList<NavigationMailboxItem> pending = mailbox.CloseAndDrain();
            foreach (NavigationMailboxItem item in pending)
            {
                CompleteClosedMailboxItem(item, finalSnapshot);
            }

            shutdown.Cancel();
            calls?.Shutdown();
            await mailboxPump;
            await WaitForActiveOperationsAsync();
            await commitGate.WaitAsync();
            state.Close();
            commitGate.Release();
            shutdown.Dispose();
            commitGate.Dispose();
        }

        private static IReadOnlyList<NavigationEntryId> CollectDepartureCandidates (NavigationState before, NavigationState after) => before.Presentations.Where(pair => pair.Value.Materialization == PresentationMaterialization.Available && (!after.Presentations.TryGetValue(pair.Key, out PresentationState? next) || next.Materialization != PresentationMaterialization.Available || next.Id != pair.Value.Id)).Select(static pair => pair.Key).ToArray();

        private static IReadOnlyCollection<RegionInstanceId> GatherSourceReadRegions (NavigationState state, IReadOnlyCollection<RegionInstanceId> planReads, SourcePrecondition source)
        {
            HashSet<RegionInstanceId> regions = new(planReads);
            RegionState current = state.GetRegion(state.GetEntry(source.EntryId).RegionId);
            while (true)
            {
                regions.Add(current.Id);
                if (!current.OwnerEntryId.HasValue)
                {
                    return regions;
                }

                current = state.GetRegion(state.GetEntry(current.OwnerEntryId.Value).RegionId);
            }
        }

        private static NavigationState ApplyDeparture (NavigationState before, IReadOnlyList<NavigationEntryId> entries)
        {
            Dictionary<NavigationEntryId, PresentationState> presentations = new(before.Presentations);
            foreach (NavigationEntryId entryId in entries)
            {
                presentations[entryId] = PresentationState.Dormant(entryId);
            }

            return before.With(before.Revision, before.Regions.ToDictionary(static item => item.Key, static item => item.Value), before.Entries.ToDictionary(static item => item.Key, static item => item.Value), presentations, NormalizeHostIncident(before.HostIncident, presentations));
        }

        private static void ValidateDepartures (IReadOnlyList<NavigationEntryId> actual, IReadOnlyList<NavigationEntryId> allowed)
        {
            if (actual.Distinct().Count() != actual.Count || actual.Any(entry => !allowed.Contains(entry)))
            {
                throw new NavigationConfigurationException("The presentation adapter selected an invalid departure set.");
            }
        }

        private NavigationState ApplyFailure (NavigationState state, PresentationFailure failure)
        {
            if (failure is null)
            {
                throw new ArgumentNullException(nameof(failure));
            }

            Dictionary<NavigationEntryId, PresentationState> presentations = new(state.Presentations);
            NavigationHostIncident? hostIncident = state.HostIncident;
            bool changed = false;
            if (failure.Scope == PresentationFailureScope.PresentationHost)
            {
                if (failure.HostIncident is not null && resolvedHostIncidents.Contains(failure.HostIncident.Id))
                {
                    return state;
                }

                if (failure.HostIncident is not null && hostIncident is not null && !hostIncident.Id.Equals(failure.HostIncident.Id))
                {
                    // A different host event cannot replace a current incident. The only
                    // permitted handoff is performed by matching Recovery's clear boundary.
                    return state;
                }
                else if (failure.HostIncident is not null)
                {
                    PresentationReference[] required = NormalizeRequiredPresentations(state, hostIncident?.RequiredPresentations ?? Array.Empty<PresentationReference>(), failure.HostIncident.RequiredPresentations);
                    NavigationHostIncident next = hostIncident is null
                        ? new NavigationHostIncident(failure.HostIncident.Id, failure.HostIncident.Reason, required)
                        : new NavigationHostIncident(hostIncident.Id, hostIncident.Reason, required);
                    changed = hostIncident is null
                        || hostIncident.RequiredPresentations.Count != next.RequiredPresentations.Count
                        || !hostIncident.RequiredPresentations.SequenceEqual(next.RequiredPresentations);
                    hostIncident = next;
                }
            }
            else if (failure.Scope == PresentationFailureScope.CurrentPresentations)
            {
                NavigationIncidentId incident = new(Guid.NewGuid());
                foreach (PresentationReference member in failure.AffectedPresentations)
                {
                    if (presentations.TryGetValue(member.EntryId, out PresentationState? stateValue)
                        && stateValue.Materialization == PresentationMaterialization.Available
                        && stateValue.Id.HasValue
                        && stateValue.Id.Value.Equals(member.PresentationId))
                    {
                        presentations[member.EntryId] = PresentationState.Lost(member.EntryId, member.PresentationId, incident);
                        changed = true;
                    }
                }
            }

            return changed
                ? state.With(state.Revision + 1, state.Regions.ToDictionary(static item => item.Key, static item => item.Value), state.Entries.ToDictionary(static item => item.Key, static item => item.Value), presentations, NormalizeHostIncident(hostIncident, presentations))
                : state;
        }

        private NavigationState PublishFailures (NavigationState current, IEnumerable<PresentationFailure> failures)
        {
            NavigationState updated = current;
            foreach (PresentationFailure failure in failures)
            {
                NavigationState next = ApplyFailure(updated, failure);
                if (!ReferenceEquals(next, updated))
                {
                    state.Publish(next);
                    updated = next;
                }
            }

            return updated;
        }

        private static PresentationFailure UnexpectedPublicationFailure (NavigationState state, NavigationPhase phase, string reason, Exception? exception = null)
        {
            NavigationHostIncident incident = new(new NavigationIncidentId(Guid.NewGuid()), reason, BuildFallbackRequiredPresentations(state));
            return new PresentationFailure(PresentationFailureScope.PresentationHost, phase, reason, Array.Empty<PresentationReference>(), null, incident) { Exception = exception };
        }

        private void NotifyDestinationObservers (bool destination, NavigationState published, NavigationOperationId operationId, NavigationDelta changes, OperationDiagnostics diagnostics)
        {
            if (destination && (changes.CreatedEntries.Count > 0 || changes.RemovedEntries.Count > 0))
            {
                NavigationCommit commit = new(published.Revision, operationId, changes, published);
                if (realizer is ScreenRuntime screens)
                {
                    screens.QueueCommit(operationId, () =>
{
    NotifyObservers(commit, diagnostics);
    return diagnostics.Snapshot();
});
                }
                else
                {
                    NotifyObservers(commit, diagnostics);
                }
            }
        }

        private void NotifyObservers (NavigationCommit commit, OperationDiagnostics diagnostics)
        {
            foreach (INavigationCommitObserver observer in observers)
            {
                try
                {
                    observer.OnCommitted(commit);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(NavigationPhase.Commit, "A navigation commit observer threw an exception: " + exception.Message, exception);
                }
            }
        }

        private static NavigationOutcome PublicationResult (NavigationOperationId operationId, NavigationOperationKind kind, bool destination, NavigationState final, NavigationDelta changes, IEnumerable<PresentationFailure> failures, IReadOnlyList<NavigationDiagnostic> diagnostics, bool hasFault)
        {
            PresentationFailure[] failureList = failures.ToArray();
            return new NavigationOutcome(operationId, kind, hasFault || failureList.Length > 0 ? NavigationOutcomeKind.CommittedWithFault : NavigationOutcomeKind.Committed, destination, final, destination ? changes : NavigationDelta.Empty, RestorationOutcome.NotRequired, ToDiagnostics(failureList).Concat(diagnostics).ToArray());
        }

        private static IReadOnlyList<NavigationDiagnostic> ToDiagnostics (IEnumerable<PresentationFailure> failures) => failures.Select(failure => new NavigationDiagnostic(
            failure.Phase,
            failure.Reason,
            failure.AffectedPresentations.Count > 0 ? failure.AffectedPresentations[0].EntryId : null,
            failure.AffectedPresentations.Count > 0 ? failure.AffectedPresentations[0].PresentationId : null)
        {
            Exception = failure.Exception
        }).ToArray();

        private static bool HasCompletionFailure (NavigationOutcome result) => result.Diagnostics.Any(static diagnostic => diagnostic.Phase == NavigationPhase.Complete);

        private static NavigationOutcome Result (NavigationOperationKind operation, NavigationOutcomeKind kind, bool destinationCommitted, NavigationState state, NavigationDelta changes, RestorationOutcome restoration, string diagnostic, NavigationOperationId? operationId = null, NavigationPhase phase = NavigationPhase.Prepare, Exception? error = null) => new(operationId ?? new NavigationOperationId(Guid.NewGuid()), operation, kind, destinationCommitted, state, changes, restoration, new[] { new NavigationDiagnostic(phase, diagnostic) { Exception = error } });
    }

}
