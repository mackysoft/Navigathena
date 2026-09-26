using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Blockers;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Messaging;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Transitions;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed partial class ScreenRuntime : IPresentationRealizer, IScreenRuntimeServices, IScreenActivityRuntime
    {
        private readonly object sync = new();
        private readonly Dictionary<PresentationId, ScreenInstance> owned = new();
        private readonly Dictionary<ScreenInstance, object> lifecycleHandlers = new();
        private readonly HashSet<Task> lossReports = new();
        private readonly ViewRegistry views = new();
        private readonly TransitionPolicy transitions;
        private readonly IReadOnlyDictionary<RegionDefinitionId, ScreenResourcePolicy> resourcePolicies;
        private readonly HashSet<TransitionPlayback> playbacks = new();
        private readonly HashSet<TransitionPlayback> running = new();
        private readonly Dictionary<NavigationOperationId, NavigationPresentationStatus> results = new();
        private readonly Queue<(NavigationOperationId Id, Func<IReadOnlyList<NavigationDiagnostic>> Notify)> pendingCommits = new();
        private readonly Dictionary<NavigationOperationId, IReadOnlyList<NavigationDiagnostic>> notificationDiagnostics = new();
        private readonly Dictionary<ScreenInstance, Task> endings = new();
        private readonly HashSet<NavigationEntryId> activatedEntries = new();
        public bool HasActivated (NavigationEntryId entry)
        {
            lock (sync)
            {
                return activatedEntries.Contains(entry);
            }
        }
        private INavigationLossSink? loss;
        private INavigationStateSource? state;
        private INavigationHostIncidentSink? incidents;
        internal CallCoordinator Calls { get; set; } = null!;
        public INavigationStateSource State => state!;
        public IScreenCallScope BindCall (NavigationEntry entry, PresentationId presentation, Func<bool> isValid)
            => Calls.Bind(entry, presentation, isValid);
        public void EndBindingCalls (NavigationEntryId entry, PresentationId presentation)
            => Calls.EndBinding(entry, presentation);
        public bool OwnsCall (NavigationEntryId entry, PresentationId presentation)
            => Calls.OwnsCall(entry, presentation);
        private TaskCompletionSource<object?> idle = CompletedIdle();

        private static TaskCompletionSource<object?> CompletedIdle ()
        {
            TaskCompletionSource<object?> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.SetResult(null);
            return signal;
        }

        public ValueTask WaitUntilIdleAsync (CancellationToken token)
        {
            lock (sync)
            {
                return new ValueTask(AsyncWait.WaitAsync(idle.Task, token).AsTask());
            }
        }

        public void ScheduleWork ()
        {
            _ = StartWorkWhenReadyAsync();
        }

        internal ValueTask MarkSettledAsync ()
        {
            lock (sync)
            {
                if (running.Count != 0)
                {
                    return default;
                }
                activatedEntries.RemoveWhere(entry => !State.Current.Entries.ContainsKey(entry));
                foreach (ScreenInstance screen in owned.Values)
                {
                    if (screen.IsActive)
                    {
                        screen.NavigationSettled = true;
                        activatedEntries.Add(screen.Entry.Id);
                    }
                }
            }
            return default;
        }

        private async Task StartWorkWhenReadyAsync ()
        {
            try
            {
                await WaitUntilIdleAsync(CancellationToken.None);
                {
                    ScreenInstance[] screens;
                    lock (sync)
                    {
                        if (running.Count != 0)
                        {
                            return;
                        }
                        screens = owned.Values.ToArray();
                    }
                    foreach (ScreenInstance screen in screens)
                    {
                        screen.StartReadyWork();
                    }
                }
            }
            catch (Exception exception)
            {
                ReportEquipmentFailure("Screen work could not be started: " + exception.Message);
            }
        }

        public ScreenRuntime (ScreenCatalog catalog, NavigationHostOptions options)
        {
            Catalog = catalog;
            transitions = new TransitionPolicy(catalog.Definition, options.Regions);
            resourcePolicies = options.Regions.ToDictionary(pair => pair.Key, pair => pair.Value.ResourcePolicy);
            if (resourcePolicies.Values.Any(policy => !Enum.IsDefined(typeof(ScreenResourcePolicy), policy)))
            {
                throw new NavigationConfigurationException("Unknown screen resource policy.");
            }

            Blockers = new BlockerCoordinator(this, views, options.DefaultBlocker);
            Orders = new PresentationOrderCoordinator(this, Blockers);
        }

        public ScreenCatalog Catalog
        {
            get;
        }
        public BlockerCoordinator Blockers
        {
            get;
        }
        public TerminationJournal Terminations { get; } = new();
        public PresentationOrderCoordinator Orders
        {
            get;
        }

        public void Bind (INavigationStateSource state, INavigationLossSink loss, INavigationHostIncidentSink incidents)
        {
            this.state = state;
            this.loss = loss;
            this.incidents = incidents;
        }

        public void AddTransitionViews (object transition, IReadOnlyList<ViewRegistration> registrations) => Orders.AddEffect(transition, registrations);
        public void RemoveTransitionViews (object transition) => Orders.RemoveEffect(transition);

        public IPresentationTransaction Begin (PresentationTransition transition, INavigationOperationProgressReporter progress)
        {
            NavigationTransition configuration = transitions.Select(transition);
            ValidateInstanceTransition(transition, configuration);
            bool outgoingFirst = RequiresOutgoingFirst(transition, configuration);

            TransitionPlayback playback = new(this, transition, configuration, views);
            lock (sync)
            {
                if (running.Any(item => item.Configuration.Scope == NavigationTransitionScope.Host) || (configuration.Scope == NavigationTransitionScope.Host && running.Count > 0))
                {
                    throw new NavigationConflictException("A host-wide transition conflicts with another running operation.");
                }

                try
                {
                    ReserveDefinitions(playback, transition);
                    Blockers.Reserve(transition.OperationId, transition.Before, transition.BeforeComposition, transition.ProposedAfter, transition.ProposedComposition);
                }
                catch
                {
                    definitionReservations.Remove(playback);
                    Blockers.Release(transition.OperationId);
                    throw;
                }
                if (running.Count == 0)
                {
                    idle = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                playbacks.Add(playback);
                running.Add(playback);
            }

            return new ScreenTransaction(this, playback, outgoingFirst ? transition.AllowedDepartureEntries : Array.Empty<NavigationEntryId>());
        }

        public bool RequiresHostReservation (PresentationTransition transition)
        {
            NavigationTransition configuration = transitions.Select(transition);
            ValidateInstanceTransition(transition, configuration);
            Blockers.Validate(transition.ProposedAfter, transition.ProposedComposition);
            return configuration.Scope == NavigationTransitionScope.Host;
        }

        private bool RequiresOutgoingFirst (PresentationTransition transition, NavigationTransition configuration)
        {
            bool outgoingFirst = transition.TargetRegion is RegionInstanceId region
                && resourcePolicies.TryGetValue(transition.Before.GetRegion(region).DefinitionId, out ScreenResourcePolicy policy)
                && policy == ScreenResourcePolicy.OutgoingFirst && transition.AllowedDepartureEntries.Count > 0;
            outgoingFirst |= (transition.Operation == NavigationOperationKind.Reload || transition.Options?.RecreateInstance == true)
                && transition.AllowedDepartureEntries.Any(entry =>
                Find(transition.Before.GetPresentation(entry)) is ScreenInstance previous
                && previous.Definition.InstancePolicy == ScreenInstancePolicy.Single
                && transition.ProposedAfter.Entries.Values.Any(next => next.ScreenDefinitionId == previous.Definition.Id
                    && transition.ProposedAfter.GetPresentation(next.Id).Materialization == PresentationMaterialization.Available
                    && transition.ProposedAfter.GetPresentation(next.Id).Id != previous.Id));
            if (outgoingFirst && (configuration.RequiresSimultaneousScreens || configuration.Source == TransitionEffectSource.SourceScreen))
            {
                throw new NavigationConfigurationException("This transition requires outgoing screens to remain alive, but their resources must be released before constructing the destination.");
            }
            return outgoingFirst;
        }

        public void Release (TransitionPlayback playback)
        {
            Blockers.Release(playback.OperationId);
            lock (sync)
            {
                running.Remove(playback);
                if (running.Count == 0)
                {
                    idle.TrySetResult(null);
                }
                definitionReservations.Remove(playback);
                if (playback.HasEnded)
                {
                    playbacks.Remove(playback);
                }
            }
        }

        public void SetResultStatus (NavigationOperationId operation, NavigationPresentationStatus status)
        {
            lock (sync)
            {
                results[operation] = status;
            }
        }

        public NavigationOutcome CompleteResult (NavigationOutcome result)
        {
            lock (sync)
            {
                if (notificationDiagnostics.TryGetValue(result.OperationId, out IReadOnlyList<NavigationDiagnostic>? diagnostics))
                {
                    notificationDiagnostics.Remove(result.OperationId);
                    result = result with
                    {
                        Diagnostics = result.Diagnostics.Concat(diagnostics).Distinct().ToArray()
                    };
                }
                if (!results.TryGetValue(result.OperationId, out NavigationPresentationStatus status))
                {
                    return result;
                }

                results.Remove(result.OperationId);
                return result with
                {
                    PresentationStatus = status
                };
            }
        }

        public void QueueCommit (NavigationOperationId operation, Func<IReadOnlyList<NavigationDiagnostic>> notification)
        {
            lock (sync)
            {
                pendingCommits.Enqueue((operation, notification));
            }
        }

        public ValueTask FlushCommitNotificationsAsync ()
        {
            while (true)
            {
                (NavigationOperationId Id, Func<IReadOnlyList<NavigationDiagnostic>> Notify) item;
                lock (sync)
                {
                    if (pendingCommits.Count == 0)
                    {
                        return default;
                    }

                    item = pendingCommits.Dequeue();
                }
                IReadOnlyList<NavigationDiagnostic> diagnostics = item.Notify();
                lock (sync)
                {
                    notificationDiagnostics[item.Id] = diagnostics;
                }
            }
        }

        public ScreenInstance? Find (PresentationState? presentation)
        {
            if (presentation?.Id is not PresentationId id)
            {
                return null;
            }

            lock (sync)
            {
                return owned.TryGetValue(id, out ScreenInstance? screen) && !screen.IsTerminated ? screen : null;
            }
        }

        public ScreenInstance Create (PresentationChange change, NavigationState candidate)
        {
            NavigationEntry entry = change.AfterEntry!;
            ScreenDefinition construction = ResolveDefinition(entry, candidate) ?? throw new NavigationConfigurationException("No screen construction definition is registered for " + entry.RouteDefinitionKey + ".");
            ScreenInstance screen = new(entry, change.Context!, Catalog.Definition.GetRoute(entry.RouteDefinitionKey.RegionId, entry.Route), construction, views, state!, ReportLoss, EndUserAsync, ClaimLifecycleHandler, this);
            lock (sync)
            {
                if (construction.InstancePolicy == ScreenInstancePolicy.Single && owned.Values.Any(item => item.Definition.Id == construction.Id && !item.IsTerminated))
                {
                    throw new NavigationConfigurationException("The single screen definition already has a live instance.");
                }
                owned.Add(screen.Id, screen);
            }

            return screen;
        }

        private void ClaimLifecycleHandler (ScreenInstance screen, object handler)
        {
            lock (sync)
            {
                if (lifecycleHandlers.Values.Any(other => ReferenceEquals(other, handler)))
                {
                    throw new NavigationConfigurationException("A lifecycle handler cannot be shared by live screen instances.");
                }
                lifecycleHandlers.Add(screen, handler);
            }
        }

        public async ValueTask PrepareAsync (ScreenInstance screen, NavigationState candidate, CancellationToken cancellationToken, ScreenPreparationReason reason = ScreenPreparationReason.NewEntry)
        {
            NavigationEntryId? parentId = candidate.GetRegion(screen.Entry.RegionId).OwnerEntryId;
            if (parentId is NavigationEntryId parent && Find(candidate.GetPresentation(parent)) is ScreenInstance owner
                && owner.Creation.ChildFactories.ContainsKey(screen.Entry.RouteDefinitionKey))
            {
                screen.Parent = owner;
            }

            await screen.PrepareAsync(cancellationToken, reason);
        }

        public void Forget (ScreenInstance screen)
        {
            if (!screen.IsTerminated)
            {
                return;
            }

            lock (sync)
            {
                owned.Remove(screen.Id);
                lifecycleHandlers.Remove(screen);
                endings.Remove(screen);
            }
        }

        public ValueTask TerminateAsync (ScreenInstance screen)
        {
            if (screen.IsTerminated)
            {
                return default;
            }

            TaskCompletionSource<object?> completion;
            lock (sync)
            {
                if (endings.TryGetValue(screen, out Task? existing))
                {
                    return new ValueTask(existing);
                }

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                endings.Add(screen, completion.Task);
            }
            screen.MarkEnding();
            Calls.TrackRetirement(screen, completion.Task);
            _ = CompleteTerminationAsync(screen, completion);
            return new ValueTask(completion.Task);
        }

        public void Retire (ScreenInstance screen)
        {
            Task termination = TerminateAsync(screen).AsTask();
            _ = termination.ContinueWith(task =>
{
    _ = task.Exception;
}, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task CompleteTerminationAsync (ScreenInstance screen, TaskCompletionSource<object?> completion)
        {
            try
            {
                await Terminations.Track(NavigationTerminationKind.Screen, screen.Entry.Id, screen.Id, null, async () =>
                {
                    await Blockers.TerminateDependentsAsync(screen);
                    ScreenInstance[] children;
                    lock (sync)
                    {
                        children = owned.Values.Where(child => !child.IsTerminated && ReferenceEquals(child.Parent, screen)).ToArray();
                    }
                    foreach (ScreenInstance child in children)
                    {
                        if (!child.IsEnding)
                        {
                            throw new InvalidOperationException("A child screen still uses this screen's resources.");
                        }

                        await TerminateAsync(child);
                    }
                    await screen.TerminateAsync();
                    Forget(screen);
                });
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }

        public async ValueTask ShutdownAsync ()
        {
            ScreenInstance[] screens;
            Task[] reports;
            TransitionPlayback[] unfinished;
            lock (sync)
            {
                screens = owned.Values.ToArray();
                reports = lossReports.ToArray();
                unfinished = playbacks.ToArray();
            }

            List<Exception> failures = new();
            try
            {
                await Blockers.ShutdownAsync();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            foreach (TransitionPlayback playback in unfinished)
            {
                failures.Add(new InvalidOperationException("Transition " + playback.OperationId + " has not finished releasing its ownership.", playback.Ended.Exception));
            }
            // Children can reference objects belonging to their parent screen.
            foreach (ScreenInstance screen in screens.OrderByDescending(screen => screen.OwnershipDepth))
            {
                if (unfinished.Any(playback => playback.Retains(screen)) || screens.Any(child => !child.IsTerminated && child.DependsOn(screen)))
                {
                    failures.Add(new InvalidOperationException("A child screen or transition still uses screen " + screen.Entry.Id + "."));
                    continue;
                }

                try
                {
                    await TerminateAsync(screen);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            Task allReports = Task.WhenAll(reports);
            try
            {
                await allReports;
            }
            catch (Exception exception)
            {
                failures.Add(allReports.Exception ?? exception);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("The host could not finish all screen ownership.", failures);
            }
        }

        private async ValueTask EndUserAsync (ScreenInstance screen)
        {
            ScreenInstance[] users = DependentScreens(screen);
            foreach (ScreenInstance user in users)
            {
                user.MarkEnding();
            }

            foreach (ScreenInstance user in users.OrderByDescending(user => user.OwnershipDepth))
            {
                await TerminateAsync(user);
            }

            await loss!.ReportAsync(new PresentationLoss(CurrentReferences(users), "The external owner ended a resource used by these screens."));
        }

        public void ReportEquipmentFailure (string reason)
        {
            Task report = HandleEquipmentFailureAsync(reason);
            TrackLossReport(report);
        }

        public ValueTask ReportUnavailableAsync (string reason) => new(HandleEquipmentFailureAsync(reason));

        private void TrackLossReport (Task report)
        {
            lock (sync)
            {
                lossReports.Add(report);
            }
            _ = report.ContinueWith(task =>
            {
                _ = task.Exception;
                lock (sync)
                {
                    lossReports.Remove(task);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task HandleEquipmentFailureAsync (string reason)
        {
            ScreenInstance[] affected;
            NavigationState snapshot = state!.Current;
            lock (sync)
            {
                affected = owned.Values.Where(screen => !screen.IsEnding && !screen.IsTerminated && snapshot.Presentations.Values.Any(presentation => presentation.Id == screen.Id)).ToArray();
            }
            {
                foreach (ScreenInstance screen in affected)
                {
                    try
                    {
                        screen.InvalidateActivity();
                    }
                    catch
                    { /* Other current views still have to be closed. */
                    }
                }
            }
            await incidents!.ReportAsync(new NavigationHostIncident(new NavigationIncidentId(Guid.NewGuid()), reason, CurrentReferences(affected)));
            foreach (ScreenInstance screen in affected)
            {
                try
                {
                    await screen.CloseActivityAsync();
                }
                catch
                { /* The incident keeps the host unavailable until recovery. */
                }
            }
        }

        private void ReportLoss (ScreenInstance screen, string reason)
        {
            ScreenInstance[] users = DependentScreens(screen);
            foreach (ScreenInstance user in users)
            {
                user.MarkEnding();
            }

            Task report = HandleLossAsync(screen, users, reason);
            TrackLossReport(report);
        }

        private async Task HandleLossAsync (ScreenInstance screen, IReadOnlyList<ScreenInstance> users, string reason)
        {
            {
                foreach (ScreenInstance user in users)
                {
                    try
                    {
                        user.InvalidateActivity();
                    }
                    catch
                    { /* Lost native outputs cannot be updated; activity permission is already invalid. */
                    }
                }
            }
            await loss!.ReportAsync(new PresentationLoss(CurrentReferences(users), reason));
            try
            {
                await screen.Lifetime.RequestEndAsync();
            }
            catch
            {
                // The failed owner's termination task retains the failure and all remaining ownership.
            }
        }

        private PresentationReference[] CurrentReferences (IEnumerable<ScreenInstance> screens)
        {
            HashSet<PresentationId> ids = new(screens.Select(screen => screen.Id));
            return state!.Current.Presentations.Values.Where(presentation => presentation.Id is PresentationId id && ids.Contains(id))
                .Select(presentation => new PresentationReference(presentation.EntryId, presentation.Id!.Value)).ToArray();
        }

        private ScreenInstance[] DependentScreens (ScreenInstance screen)
        {
            lock (sync)
            {
                return owned.Values.Where(user => ReferenceEquals(user, screen) || user.DependsOn(screen)).Append(screen).Distinct().ToArray();
            }
        }

        internal static int Depth (RegionInstanceId regionId, NavigationState state)
        {
            int depth = 0;
            while (state.Regions.TryGetValue(regionId, out RegionState? region) && region.OwnerEntryId is NavigationEntryId parent && state.Entries.TryGetValue(parent, out NavigationEntry? entry))
            {
                depth++;
                regionId = entry.RegionId;
            }

            return depth;
        }

        internal static bool IsDescendant (RegionInstanceId regionId, NavigationEntryId ancestor, NavigationState state)
        {
            while (state.Regions.TryGetValue(regionId, out RegionState? region) && region.OwnerEntryId is NavigationEntryId parent && state.Entries.TryGetValue(parent, out NavigationEntry? entry))
            {
                if (parent == ancestor)
                {
                    return true;
                }

                regionId = entry.RegionId;
            }

            return false;
        }
    }
}
