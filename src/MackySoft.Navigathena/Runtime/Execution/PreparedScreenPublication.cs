using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Blockers;
using MackySoft.Navigathena.Runtime.Publication;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Transitions;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed class PreparedScreenPublication : IPreparedPublication
    {
        private readonly ScreenRuntime runtime;
        private readonly PresentationUpdate update;
        private readonly TransitionPlayback playback;
        private readonly Dictionary<ScreenInstance, ViewPresentation> before = new();
        private readonly Dictionary<ScreenInstance, ScreenAnimationState> appearanceBefore = new();
        private readonly HashSet<ScreenInstance> activeBefore = new();
        private readonly List<ScreenInstance> created = new();
        private readonly Dictionary<ScreenInstance, (NavigationEntry Entry, IScreenNavigation Navigation)> rebound = new();
        private readonly List<BlockerCoordinator.SuspendedConnection> suspendedBlockers = new();
        private readonly Dictionary<ScreenInstance, IDisposable> users = new();
        private readonly List<PresentationStateCapture> captures = new();
        private readonly List<PresentationFailure> failures = new();
        private NavigationState? committed;
        private EffectiveComposition? composition;
        private bool completed;
        private bool disposed;
        private BlockerCoordinator.BlockerUpdate? blockers;

        public PreparedScreenPublication (ScreenRuntime runtime, PresentationUpdate update, TransitionPlayback playback)
        {
            this.runtime = runtime;
            this.update = update;
            this.playback = playback;
        }

        public async ValueTask PrepareAsync (CancellationToken cancellationToken)
        {
            runtime.SetResultStatus(playback.OperationId, NavigationPresentationStatus.RecoveryRequired);
            HashSet<RegionInstanceId> regions = new(update.Changes.Where(change => change.Before?.Id != change.After?.Id
                || change.BeforeParticipation?.OutputPresented != change.AfterParticipation?.OutputPresented
                || change.BeforeParticipation?.SemanticInputEligible != change.AfterParticipation?.SemanticInputEligible)
                .Select(change => (change.AfterEntry ?? change.BeforeEntry)!.RegionId));
            foreach (NavigationEntry entry in update.Before.Entries.Values)
            {
                if (update.ProposedAfter.Entries.ContainsKey(entry.Id)
                    && Appearance(entry.Id, update.BeforeComposition) != Appearance(entry.Id, update.ProposedComposition))
                {
                    regions.Add(entry.RegionId);
                }
            }
            foreach (NavigationEntry entry in update.Before.Entries.Values.Where(entry => playback.Configuration.Scope == NavigationTransitionScope.Host || regions.Contains(entry.RegionId)))
            {
                ScreenInstance? screen = runtime.Find(update.Before.GetPresentation(entry.Id));
                if (screen is null || screen.IsEnding)
                {
                    continue;
                }

                users.Add(screen, screen.Use());
                screen.NavigationSettled = false;
                screen.PreparationReason = null;
                before.Add(screen, screen.Presentation);
                appearanceBefore.Add(screen, Appearance(screen.Entry.Id, update.BeforeComposition));
                if (screen.IsActive)
                {
                    activeBefore.Add(screen);
                }

            }

            List<Exception> inputFailures = new();
            try
            {
                runtime.Blockers.CloseInput(before.Keys);
            }
            catch (Exception exception)
            {
                inputFailures.Add(exception);
            }
            foreach (ScreenInstance screen in before.Keys)
            {
                try
                {
                    screen.Apply(new ViewPresentation(screen.Presentation.OutputEnabled, false, screen.Presentation.Order));
                }
                catch (Exception exception)
                {
                    inputFailures.Add(exception);
                }
            }
            foreach (ScreenInstance screen in before.Keys)
            {
                if (playback.Configuration.Scope == NavigationTransitionScope.Host || !WillRetain(screen, update.ProposedAfter)
                    || GetParticipation(screen, update.ProposedComposition)?.SemanticInputEligible != true
                    || (GetReturnOptions(screen) is ScreenHistoryReturnOptions returning
                        && (returning.Preparation == ScreenPreparationMode.Always || returning.EnterAnimation == ScreenEnterAnimationMode.Always)))
                {
                    try
                    {
                        await screen.CloseActivityAsync();
                    }
                    catch (Exception exception)
                    {
                        inputFailures.Add(exception);
                    }
                }
            }
            if (inputFailures.Count > 0)
            {
                throw new AggregateException("Screen input or activity could not be closed.", inputFailures);
            }

            foreach (ScreenInstance screen in before.Keys)
            {
                if (!WillRetain(screen, update.ProposedAfter) && update.ProposedAfter.Entries.ContainsKey(screen.Entry.Id) && screen.CaptureState() is PresentationStateCapture capture)
                {
                    captures.Add(capture);
                }
            }

            await playback.BeginAsync(false, cancellationToken);

            PresentationChange[] rebinding = update.Changes.Where(change => change.After?.Materialization == PresentationMaterialization.Available
                && change.Context is not null && runtime.Find(change.After) is ScreenInstance screen && screen.Entry.Id != change.AfterEntry!.Id).ToArray();
            if (rebinding.Length > 0)
            {
                await playback.PrepareSwitchAsync(update, cancellationToken);
                playback.BeginRebinding();
                foreach (PresentationChange change in rebinding.OrderByDescending(change => ScreenRuntime.Depth(change.AfterEntry!.RegionId, update.ProposedAfter)))
                {
                    ScreenInstance screen = runtime.Find(change.After)!;
                    PresentationStateCapture? capture = captures.FirstOrDefault(item => item.EntryId == screen.Entry.Id) ?? screen.CaptureState();
                    rebound.Add(screen, (screen.Entry with
                    {
                        SavedState = capture is null ? screen.Entry.SavedState : capture.Value
                    }, screen.Navigation));
                    await PlayDepartureAsync(screen, update.ProposedAfter, cancellationToken);
                    if (runtime.Blockers.CaptureConnection(screen) is BlockerCoordinator.SuspendedConnection connection)
                    {
                        suspendedBlockers.Add(connection);
                        await connection.DisconnectAsync();
                    }
                    await runtime.Blockers.TerminateDependentsAsync(screen);
                    foreach (ScreenInstance child in before.Keys.Where(child => ScreenRuntime.IsDescendant(child.Entry.RegionId, screen.Entry.Id, update.Before)).OrderByDescending(child => child.OwnershipDepth))
                    {
                        ReleaseUse(child);
                        await runtime.TerminateAsync(child);
                    }
                    await screen.RebindAsync(change.AfterEntry!, change.Context!, cancellationToken, PreparationFor(change.AfterEntry!));
                }
            }

            ScreenInstance[] reentering = before.Keys.Where(screen => !rebound.ContainsKey(screen)
                && WillRetain(screen, update.ProposedAfter)
                && GetReturnOptions(screen)?.Preparation == ScreenPreparationMode.Always).ToArray();
            if (reentering.Length > 0)
            {
                if (rebinding.Length == 0)
                {
                    await playback.PrepareSwitchAsync(update, cancellationToken);
                }
                playback.BeginRebinding();
                foreach (ScreenInstance screen in reentering)
                {
                    PresentationStateCapture? capture = screen.CaptureState();
                    NavigationEntry saved = screen.Entry with
                    {
                        SavedState = capture is null ? screen.Entry.SavedState : capture.Value
                    };
                    if (capture is not null)
                    {
                        captures.Add(capture);
                    }
                    rebound.Add(screen, (saved, screen.Navigation));
                    if (runtime.Blockers.CaptureConnection(screen) is BlockerCoordinator.SuspendedConnection connection)
                    {
                        suspendedBlockers.Add(connection);
                        await connection.DisconnectAsync();
                    }
                    await runtime.Blockers.TerminateDependentsAsync(screen);
                    screen.Apply(new ViewPresentation(false, false, screen.Presentation.Order));
                    await screen.RebindAsync(saved, screen.Navigation, cancellationToken, ScreenPreparationReason.Reentry);
                }
            }

            foreach (PresentationChange change in update.Changes.OrderBy(change => ScreenRuntime.Depth((change.AfterEntry ?? change.BeforeEntry)!.RegionId, update.ProposedAfter)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (change.After?.Materialization != PresentationMaterialization.Available || change.Context is null || runtime.Find(change.After) is not null)
                {
                    continue;
                }
                PresentationChange construction = change;
                if (playback.Operation == NavigationOperationKind.Reload && playback.ReloadOptions?.RestoreState == true
                    && captures.FirstOrDefault(capture => capture.EntryId == change.AfterEntry!.Id) is PresentationStateCapture saved)
                {
                    construction = change with
                    {
                        AfterEntry = change.AfterEntry! with
                        {
                            SavedState = saved.Value
                        }
                    };
                }
                ScreenInstance screen = runtime.Create(construction, update.ProposedAfter);
                created.Add(screen);
                users.Add(screen, screen.Use());
                await runtime.PrepareAsync(screen, update.ProposedAfter, cancellationToken, PreparationFor(screen.Entry));
            }

            if (update.Kind != PresentationUpdateKind.Departure)
            {
                if (update.Kind != PresentationUpdateKind.Restoration)
                {
                    await playback.BeginAsync(true, cancellationToken);
                }
                if ((rebinding.Length == 0 && reentering.Length == 0) || playback.Configuration.Source == TransitionEffectSource.DestinationScreen)
                {
                    await playback.PrepareSwitchAsync(update, cancellationToken);
                }
                blockers = await runtime.Blockers.PrepareAsync(playback.OperationId, update, cancellationToken);
            }

            foreach (ScreenInstance screen in created.Concat(before.Keys))
            {
                if (screen.IsTerminated)
                {
                    continue;
                }
                foreach (var view in screen.Creation.Registrations)
                {
                    view.Validate(new ViewPresentation(GetParticipation(screen, update.ProposedComposition)?.OutputPresented == true, false, view.Order));
                }

                if (GetParticipation(screen, update.ProposedComposition)?.OutputPresented == true && !screen.Presentation.OutputEnabled)
                {
                    screen.Creation.Animator?.SetStateImmediately(created.Contains(screen) || rebound.ContainsKey(screen)
                        ? ScreenAnimationState.BeforeEnter : appearanceBefore[screen]);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public ValueTask<PublicationOutcome> CommitAsync (INavigationCommit commit, CancellationToken shutdownToken)
        {
            runtime.Orders.Validate(commit.Candidate, commit.CandidateComposition);
            ScreenInstance[] ordered = commit.CandidateComposition.Presentations
                .Select(item => runtime.Find(commit.Candidate.GetPresentation(item.EntryId)))
                .Where(screen => screen is not null && !screen.IsEnding).Cast<ScreenInstance>().ToArray();
            foreach (ScreenInstance screen in ordered)
            {
                foreach (var view in screen.Creation.Registrations)
                {
                    view.Validate(new ViewPresentation(screen.Presentation.OutputEnabled, screen.Presentation.InputEnabled, view.Order));
                }
            }

            foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => WillRetain(screen, commit.Candidate)))
            {
                if (screen.IsEnding || screen.Creation.Registrations.Any(view => !view.Adapter.IsAlive))
                {
                    return new ValueTask<PublicationOutcome>(PublicationOutcome.Conflict);
                }
            }

            if (commit is NavigationCommitCapability capability)
            {
                capability.SelectScreenDefinitions(ordered.ToDictionary(screen => screen.Entry.Id, screen => screen.Definition.Id));
                capability.IncludeRepreparedEntries(rebound.Where(pair => pair.Key.Entry.Id == pair.Value.Entry.Id).Select(pair => pair.Key.Entry.Id));
            }
            commit.Apply(playback.Operation == NavigationOperationKind.Reload && update.Kind == PresentationUpdateKind.Destination && playback.ReloadOptions?.RestoreState != true
                ? Array.Empty<PresentationStateCapture>() : captures);
            committed = commit.Candidate;
            runtime.Calls.Commit(committed, playback.CallChange, playback.Operation);
            if (update.Kind == PresentationUpdateKind.Departure)
            {
                playback.Departed = true;
            }

            if (update.Kind == PresentationUpdateKind.Destination)
            {
                playback.DestinationCommitted = true;
            }

            composition = commit.CandidateComposition;
            foreach (ScreenInstance screen in ordered.Where(screen => !created.Contains(screen) && !before.ContainsKey(screen)))
            {
                try
                {
                    screen.Apply(screen.Presentation);
                }
                catch (Exception exception)
                {
                    failures.Add(Failure(screen, exception));
                }
            }
            try
            {
                runtime.Orders.Apply(committed, composition);
                blockers?.Commit(committed);
            }
            catch (Exception exception)
            {
                foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => WillRetain(screen, committed)))
                {
                    failures.Add(Failure(screen, exception));
                }
            }
            foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => !screen.IsTerminated))
            {
                try
                {
                    bool present = WillRetain(screen, committed) && GetParticipation(screen, composition)?.OutputPresented == true;
                    // Keep outgoing output until its exit animation has completed.
                    screen.Apply(new ViewPresentation(present || screen.Presentation.OutputEnabled, false, screen.Presentation.Order));
                }
                catch (Exception exception)
                {
                    failures.Add(Failure(screen, exception));
                }
            }

            return new ValueTask<PublicationOutcome>(PublicationOutcome.Applied(failures.ToArray()));
        }

        public async ValueTask<PresentationCompletion> CompleteAsync (CancellationToken shutdownToken)
        {
            if (committed is null || composition is null)
            {
                throw new InvalidOperationException("A screen publication must commit before completion.");
            }

            try
            {
                foreach (PresentationChangeContext change in update.RetainedChanges)
                {
                    ScreenInstance? retained = runtime.Find(change.Self.After);
                    if (retained is null)
                    {
                        continue;
                    }

                    try
                    {
                        retained.NotifyChange(change);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(Failure(retained, exception));
                    }
                }
                if (update.Kind == PresentationUpdateKind.Departure)
                {
                    foreach (ScreenInstance screen in before.Keys.Where(screen => !WillRetain(screen, committed)).OrderByDescending(screen => screen.OwnershipDepth))
                    {
                        await PlayDepartureAsync(screen, committed, shutdownToken);
                        ReleaseUse(screen);
                        await runtime.TerminateAsync(screen);
                    }
                    completed = true;
                    return new PresentationCompletion(failures.ToArray());
                }
                if (failures.Count == 0)
                {
                    List<Func<CancellationToken, Task>> animations = new();
                    foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => !screen.IsTerminated))
                    {
                        animations.Add(token => AnimateChangeAsync(screen, token));
                    }
                    animations.Add(token => playback.AfterCommitAsync(update, token).AsTask());
                    await ScreenAnimationBatch.RunAsync(animations, shutdownToken);
                }

                await playback.FinishAsync(failures.Count == 0 ? (update.Kind == PresentationUpdateKind.Restoration ? TransitionSettlementTarget.Source : TransitionSettlementTarget.Destination) : TransitionSettlementTarget.Unavailable, playback.DestinationCommitted);

                foreach (ScreenInstance screen in rebound.Keys)
                {
                    await screen.ReleasePreviousInputAsync();
                }

                ScreenInstance[] retired = before.Keys.Where(screen => !WillRetain(screen, committed)).OrderByDescending(screen => ScreenRuntime.Depth(screen.Entry.RegionId, update.Before)).ToArray();
                foreach (ScreenInstance screen in retired)
                {
                    ReleaseUse(screen);
                    try
                    {
                        if (playback.WaitForTermination)
                        {
                            await runtime.TerminateAsync(screen);
                        }
                        else
                        {
                            runtime.Retire(screen);
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new PresentationFailure(PresentationFailureScope.RetiredResources, NavigationPhase.Complete, exception.Message, Array.Empty<PresentationReference>()) { Exception = exception });
                    }
                }

                if (failures.All(failure => failure.Scope == PresentationFailureScope.RetiredResources))
                {
                    foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => WillRetain(screen, committed) && GetParticipation(screen, composition)?.SemanticInputEligible == true))
                    {
                        await screen.ActivateAsync(ActivationFor(screen.Entry));
                    }

                    foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => WillRetain(screen, committed)))
                    {
                        PresentationParticipation? participation = GetParticipation(screen, composition);
                        screen.Apply(new ViewPresentation(participation?.OutputPresented == true, participation?.OutputPresented == true && screen.IsActive, screen.Presentation.Order));
                    }
                    blockers?.OpenInput();
                }
                try
                {
                    runtime.Orders.Refresh();
                }
                catch (Exception exception)
                {
                    failures.Add(new PresentationFailure(PresentationFailureScope.RetiredResources, NavigationPhase.Complete, exception.Message, Array.Empty<PresentationReference>()) { Exception = exception });
                }
            }
            catch (Exception exception)
            {
                try
                {
                    blockers?.CloseInput();
                }
                catch (Exception blockerFailure)
                {
                    await runtime.ReportUnavailableAsync(blockerFailure.Message);
                }
                try
                {
                    await playback.FinishAsync(TransitionSettlementTarget.Unavailable, playback.DestinationCommitted);
                }
                catch (Exception settlementFailure)
                {
                    failures.Add(new PresentationFailure(PresentationFailureScope.RetiredResources, NavigationPhase.Complete, settlementFailure.Message, Array.Empty<PresentationReference>()) { Exception = settlementFailure });
                }

                foreach (ScreenInstance screen in created.Concat(before.Keys).Where(screen => WillRetain(screen, committed)))
                {
                    try
                    {
                        await screen.CloseActivityAsync();
                    }
                    catch (Exception stopFailure)
                    {
                        failures.Add(Failure(screen, stopFailure));
                    }

                    failures.Add(Failure(screen, exception));
                }
            }

            completed = true;
            runtime.SetResultStatus(playback.OperationId, failures.All(failure => failure.Scope == PresentationFailureScope.RetiredResources) ? NavigationPresentationStatus.Ready : NavigationPresentationStatus.RecoveryRequired);
            return new PresentationCompletion(failures.ToArray());
        }

        public async ValueTask DisposeAsync ()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (IDisposable usage in users.Values)
            {
                usage.Dispose();
            }

            users.Clear();
            if (committed is not null)
            {
                return;
            }

            List<Exception> errors = new();
            if (before.Keys.Any(screen => screen.IsTerminated))
            {
                errors.Add(new InvalidOperationException("Child instances ended during rebinding and require restoration."));
            }
            try
            {
                if (!playback.Departed)
                {
                    await playback.FinishAsync(TransitionSettlementTarget.Source, false);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                await runtime.Blockers.DiscardAsync(playback.OperationId);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            foreach (ScreenInstance screen in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (playback.Retains(screen))
                    {
                        throw new InvalidOperationException("An unfinished transition still uses the prepared screen.");
                    }

                    await runtime.TerminateAsync(screen);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            foreach (ScreenInstance screen in before.Keys)
            {
                try
                {
                    if (screen.IsEnding || playback.Retains(screen))
                    {
                        if (rebound.ContainsKey(screen))
                        {
                            errors.Add(new InvalidOperationException("The rebound screen is unavailable and cannot restore its old history input."));
                        }
                        continue;
                    }

                    if (rebound.TryGetValue(screen, out var original))
                    {
                        await screen.RebindAsync(original.Entry, original.Navigation, CancellationToken.None);
                        await screen.ReleasePreviousInputAsync();
                    }
                    screen.Creation.Animator?.SetStateImmediately(appearanceBefore[screen]);
                    if (activeBefore.Contains(screen))
                    {
                        await screen.ActivateAsync();
                    }

                    screen.Apply(before[screen]);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (errors.Count > 0)
            {
                await runtime.ReportUnavailableAsync("Screen preparation could not be fully rolled back: " + errors[0].Message);
                throw new AggregateException("Screen preparation could not be fully rolled back.", errors);
            }

            try
            {
                foreach (BlockerCoordinator.SuspendedConnection connection in suspendedBlockers)
                {
                    connection.Restore();
                }
                blockers?.Rollback();
                runtime.Blockers.RestoreInput(before.Keys);
            }
            catch (Exception exception)
            {
                await runtime.ReportUnavailableAsync(exception.Message);
                throw;
            }

            runtime.SetResultStatus(playback.OperationId, update.Before.HostIncident is not null || update.Before.Presentations.Values.Any(presentation => presentation.Materialization == PresentationMaterialization.Lost) ? NavigationPresentationStatus.RecoveryRequired : NavigationPresentationStatus.Ready);
        }

        private void ReleaseUse (ScreenInstance screen)
        {
            if (users.TryGetValue(screen, out IDisposable? usage))
            {
                usage.Dispose();
                users.Remove(screen);
            }
        }

        private NavigationEntryId? ReturnEntry
        {
            get
            {
                if (playback.Operation != NavigationOperationKind.Back || update.Kind == PresentationUpdateKind.Restoration
                    || playback.TargetRegion is not RegionInstanceId target || !update.ProposedAfter.Regions.TryGetValue(target, out RegionState? region))
                {
                    return null;
                }
                return region.Entries.Count > 0 ? region.Entries[region.Entries.Count - 1] : null;
            }
        }

        private bool IsHistoryReturn (NavigationEntry entry)
            => ReturnEntry is NavigationEntryId destination && update.Before.Entries.ContainsKey(entry.Id)
                && (entry.Id == destination || ScreenRuntime.IsDescendant(entry.RegionId, destination, update.ProposedAfter));

        private ScreenHistoryReturnOptions? GetReturnOptions (ScreenInstance screen)
        {
            if (!IsHistoryReturn(screen.Entry))
            {
                return null;
            }
            ScreenHistoryReturnOptions defaults = screen.Definition.HistoryReturn;
            return ReturnEntry == screen.Entry.Id && playback.BackOptions is BackOptions options ? options.Apply(defaults) : defaults;
        }

        private ScreenPreparationReason PreparationFor (NavigationEntry entry)
            => update.Kind == PresentationUpdateKind.Restoration || playback.Operation == NavigationOperationKind.Recovery
                ? ScreenPreparationReason.Recovery
                : playback.Operation == NavigationOperationKind.Reload ? ScreenPreparationReason.Reload
                : update.Before.Entries.ContainsKey(entry.Id) ? ScreenPreparationReason.HistoryRestoration : ScreenPreparationReason.NewEntry;

        private ScreenActivationReason ActivationFor (NavigationEntry entry)
            => update.Kind == PresentationUpdateKind.Restoration || playback.Operation == NavigationOperationKind.Recovery
                ? ScreenActivationReason.Recovery
                : playback.Operation == NavigationOperationKind.Reload ? ScreenActivationReason.Reload
                : IsHistoryReturn(entry) ? ScreenActivationReason.HistoryReturn
                : update.Before.Entries.ContainsKey(entry.Id) ? ScreenActivationReason.Resume : ScreenActivationReason.Entry;

        private Task PlayDepartureAsync (ScreenInstance screen, NavigationState destination, CancellationToken cancellationToken)
        {
            bool retainedEntry = playback.Operation != NavigationOperationKind.Reload && destination.Entries.ContainsKey(screen.Entry.Id);
            return PlayAnimationAsync(screen, new ScreenAnimation(retainedEntry ? ScreenAnimationKind.Cover : ScreenAnimationKind.Exit,
                appearanceBefore[screen], retainedEntry ? ScreenAnimationState.Hidden : ScreenAnimationState.AfterExit), screen.Presentation.OutputEnabled, cancellationToken);
        }

        private Task AnimateChangeAsync (ScreenInstance screen, CancellationToken cancellationToken)
        {
            if (!WillRetain(screen, committed!))
            {
                return PlayDepartureAsync(screen, committed!, cancellationToken);
            }
            ScreenAnimationState after = Appearance(screen.Entry.Id, composition!);
            bool constructed = created.Contains(screen) || rebound.ContainsKey(screen);
            ScreenAnimationState previous = constructed ? ScreenAnimationState.BeforeEnter : appearanceBefore[screen];
            bool newEntry = !update.Before.Entries.ContainsKey(screen.Entry.Id);
            ScreenAnimationKind kind = constructed && (newEntry || playback.Operation == NavigationOperationKind.Reload) ? ScreenAnimationKind.Enter
                : after == ScreenAnimationState.Hidden || (after == ScreenAnimationState.Background && previous == ScreenAnimationState.Foreground)
                    ? ScreenAnimationKind.Cover : ScreenAnimationKind.Reveal;
            bool animate = previous != after;
            ScreenEnterAnimationMode? mode = GetReturnOptions(screen)?.EnterAnimation;
            if (mode == ScreenEnterAnimationMode.Skip)
            {
                animate = false;
            }
            else if (mode == ScreenEnterAnimationMode.Always)
            {
                animate = after != ScreenAnimationState.Hidden;
            }
            else if (mode == ScreenEnterAnimationMode.WhenShown)
            {
                animate = after != ScreenAnimationState.Hidden && (!before.TryGetValue(screen, out ViewPresentation old) || !old.OutputEnabled);
            }
            return PlayAnimationAsync(screen, new ScreenAnimation(kind, previous, after), animate, cancellationToken);
        }

        private static ScreenAnimationState Appearance (NavigationEntryId entry, EffectiveComposition composition)
        {
            PresentationParticipation? participation = composition.Presentations.FirstOrDefault(item => item.EntryId == entry)?.Participation;
            if (participation?.OutputPresented != true)
            {
                return ScreenAnimationState.Hidden;
            }
            return participation.Foreground ? ScreenAnimationState.Foreground : ScreenAnimationState.Background;
        }

        private static async Task PlayAnimationAsync (ScreenInstance screen, ScreenAnimation animation, bool animate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (screen.Creation.Animator is IScreenAnimator animator)
            {
                if (animate)
                {
                    await animator.PlayAsync(animation, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                animator.SetStateImmediately(animation.To);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (animation.To == ScreenAnimationState.Hidden || animation.To == ScreenAnimationState.AfterExit)
            {
                screen.Apply(new ViewPresentation(false, false, screen.Presentation.Order));
            }
        }

        private PresentationFailure Failure (ScreenInstance screen, Exception exception) => WillRetain(screen, committed ?? update.ProposedAfter)
            ? new PresentationFailure(PresentationFailureScope.CurrentPresentations, completed ? NavigationPhase.Complete : NavigationPhase.Commit, exception.Message, new[] { new PresentationReference(screen.Entry.Id, screen.Id) }) { Exception = exception }
            : new PresentationFailure(PresentationFailureScope.RetiredResources, NavigationPhase.Complete, exception.Message, Array.Empty<PresentationReference>()) { Exception = exception };

        private static bool WillRetain (ScreenInstance screen, NavigationState state) => state.Presentations.TryGetValue(screen.Entry.Id, out PresentationState? presentation) && presentation.Materialization == PresentationMaterialization.Available && presentation.Id == screen.Id;
        private static PresentationParticipation? GetParticipation (ScreenInstance screen, EffectiveComposition composition) => composition.Presentations.FirstOrDefault(item => item.EntryId == screen.Entry.Id)?.Participation;
    }
}
