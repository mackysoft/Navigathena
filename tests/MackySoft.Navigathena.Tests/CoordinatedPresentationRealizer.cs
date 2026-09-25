using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Tests;

internal sealed class CoordinatedPresentationRealizer : IPresentationRealizer
{
    private readonly List<CommittedPublication> committedPublications = new();
    private readonly List<PresentationContext> contexts = new();
    private readonly List<PresentationTransition> transitions = new();
    private readonly List<PresentationUpdateKind> updateKinds = new();
    private readonly List<PresentationUpdate> updates = new();
    private TaskCompletionSource<bool>? blockedPreparation;
    private TaskCompletionSource<bool>? releasePreparation;
    private TaskCompletionSource<bool>? blockedDestinationApply;
    private TaskCompletionSource<bool>? releaseDestinationApply;
    private TaskCompletionSource<bool>? blockedRestorationCommit;
    private TaskCompletionSource<bool>? releaseRestorationCommit;
    private TaskCompletionSource<bool>? blockedCompletion;
    private TaskCompletionSource<bool>? releaseCompletion;
    private TaskCompletionSource<bool>? shutdownObservedWhileCompletionBlocked;
    private int blockNextPreparation;
    private int blockNextDestinationApply;
    private int blockNextRestorationCommit;
    private int blockNextCompletion;

    public bool DepartAllowedEntries
    {
        get; set;
    }
    public DestinationCommitBehavior DestinationCommitBehavior
    {
        get; set;
    }
    public PresentationFailure? DestinationCompletionFailure
    {
        get; set;
    }
    public PresentationFailureScope? DestinationFailureScope
    {
        get; set;
    }
    public bool FailDepartureCompletion
    {
        get; set;
    }
    public bool FailHostRecoveryAfterApply
    {
        get; set;
    }
    public bool IgnoreShutdownWhileCompletionIsBlocked
    {
        get; set;
    }
    public INavigationCommit? LastDestinationCommit
    {
        get; private set;
    }
    public bool RejectDestinationCommit
    {
        get; set;
    }
    public bool RejectRestorationCommit
    {
        get; set;
    }
    public int PublicationDisposeCount
    {
        get; private set;
    }
    public bool PreparationCancellationObserved
    {
        get; private set;
    }
    public bool ThrowOnDestinationCompletion
    {
        get; set;
    }
    public int TransactionDisposeCount
    {
        get; private set;
    }
    public bool CompletionCancellationObserved
    {
        get; private set;
    }

    public IReadOnlyList<CommittedPublication> CommittedPublications
    {
        get
        {
            lock (committedPublications)
            {
                return committedPublications.ToArray();
            }
        }
    }

    public IReadOnlyList<PresentationContext> Contexts
    {
        get
        {
            lock (contexts)
            {
                return contexts.ToArray();
            }
        }
    }

    public IReadOnlyList<PresentationTransition> Transitions
    {
        get
        {
            lock (transitions)
            {
                return transitions.ToArray();
            }
        }
    }

    public IReadOnlyList<PresentationUpdateKind> UpdateKinds
    {
        get
        {
            lock (updateKinds)
            {
                return updateKinds.ToArray();
            }
        }
    }

    public IReadOnlyList<PresentationUpdate> Updates
    {
        get
        {
            lock (updates)
            {
                return updates.ToArray();
            }
        }
    }

    public Task BlockNextPreparationAsync ()
    {
        blockedPreparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        releasePreparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref blockNextPreparation, 1);
        return blockedPreparation.Task;
    }

    public void ReleaseBlockedPreparation ()
    {
        releasePreparation!.TrySetResult(true);
    }

    public Task BlockNextDestinationApplyAsync ()
    {
        blockedDestinationApply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        releaseDestinationApply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref blockNextDestinationApply, 1);
        return blockedDestinationApply.Task;
    }

    public void ReleaseBlockedDestinationApply ()
    {
        releaseDestinationApply!.TrySetResult(true);
    }

    public Task BlockNextRestorationCommitAsync ()
    {
        blockedRestorationCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        releaseRestorationCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref blockNextRestorationCommit, 1);
        return blockedRestorationCommit.Task;
    }

    public Task BlockNextCompletionAsync ()
    {
        blockedCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        releaseCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        shutdownObservedWhileCompletionBlocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref blockNextCompletion, 1);
        return blockedCompletion.Task;
    }

    public Task WaitForShutdownWhileCompletionIsBlockedAsync () => shutdownObservedWhileCompletionBlocked!.Task;

    public void ReleaseBlockedCompletion ()
    {
        releaseCompletion!.TrySetResult(true);
    }

    public IPresentationTransaction Begin (PresentationTransition transition, INavigationOperationProgressReporter progress)
    {
        lock (transitions)
        {
            transitions.Add(transition);
        }

        IReadOnlyList<NavigationEntryId> departures = DepartAllowedEntries
            ? transition.AllowedDepartureEntries
            : Array.Empty<NavigationEntryId>();
        return new Transaction(this, departures);
    }

    private sealed class Transaction : IPresentationTransaction
    {
        private readonly CoordinatedPresentationRealizer owner;

        public Transaction (CoordinatedPresentationRealizer owner, IReadOnlyList<NavigationEntryId> departureEntries)
        {
            this.owner = owner;
            DepartureEntries = departureEntries;
        }

        public IReadOnlyList<NavigationEntryId> DepartureEntries
        {
            get;
        }

        public ValueTask DisposeAsync ()
        {
            owner.TransactionDisposeCount++;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<IPreparedPublication> PrepareAsync (PresentationUpdate update, CancellationToken cancellationToken)
        {
            lock (owner.updateKinds)
            {
                owner.updateKinds.Add(update.Kind);
            }

            lock (owner.updates)
            {
                owner.updates.Add(update);
            }

            lock (owner.contexts)
            {
                owner.contexts.AddRange(update.Changes.Where(change => change.Context is not null).Select(change => change.Context!));
            }

            if (Interlocked.Exchange(ref owner.blockNextPreparation, 0) != 0)
            {
                owner.blockedPreparation!.TrySetResult(true);
                try
                {
                    await owner.releasePreparation!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    owner.PreparationCancellationObserved = true;
                    throw;
                }
            }

            return new Publication(owner, update);
        }
    }

    private sealed class Publication : IPreparedPublication
    {
        private readonly CoordinatedPresentationRealizer owner;
        private readonly PresentationUpdate update;
        private readonly PresentationUpdateKind kind;

        public Publication (CoordinatedPresentationRealizer owner, PresentationUpdate update)
        {
            this.owner = owner;
            this.update = update;
            kind = update.Kind;
        }

        public async ValueTask<PublicationOutcome> CommitAsync (INavigationCommit commit, CancellationToken shutdownToken)
        {
            if (kind == PresentationUpdateKind.Destination)
            {
                owner.LastDestinationCommit = commit;
                if (owner.DestinationCommitBehavior == DestinationCommitBehavior.ThrowBeforeApply)
                {
                    throw new InvalidOperationException("The destination commit failed before it was applied.");
                }

                if (owner.DestinationCommitBehavior == DestinationCommitBehavior.ReturnAppliedBeforeApply)
                {
                    return PublicationOutcome.Applied();
                }

                if (owner.DestinationCommitBehavior == DestinationCommitBehavior.ReturnHostIncidentBeforeApply)
                {
                    return PublicationOutcome.BlockedByHostIncident(new NavigationHostIncident(
                        new NavigationIncidentId(Guid.NewGuid()),
                        "The root equipment could not be restored to its pre-commit state.",
                        Array.Empty<PresentationReference>()));
                }
            }

            if (kind == PresentationUpdateKind.Restoration && owner.RejectRestorationCommit)
            {
                return PublicationOutcome.Conflict;
            }

            if (kind == PresentationUpdateKind.Restoration && Interlocked.Exchange(ref owner.blockNextRestorationCommit, 0) != 0)
            {
                owner.blockedRestorationCommit!.TrySetResult(true);
                await owner.releaseRestorationCommit!.Task.WaitAsync(shutdownToken).ConfigureAwait(false);
            }

            if (kind == PresentationUpdateKind.Destination && owner.RejectDestinationCommit)
            {
                return PublicationOutcome.Conflict;
            }

            if (kind == PresentationUpdateKind.Destination && Interlocked.Exchange(ref owner.blockNextDestinationApply, 0) != 0)
            {
                owner.blockedDestinationApply!.TrySetResult(true);
                await owner.releaseDestinationApply!.Task.WaitAsync(shutdownToken).ConfigureAwait(false);
            }

            commit.Apply(Array.Empty<PresentationStateCapture>());
            lock (owner.committedPublications)
            {
                owner.committedPublications.Add(new CommittedPublication(update, commit.Candidate, commit.CandidateComposition));
            }

            if (kind == PresentationUpdateKind.Restoration && owner.FailHostRecoveryAfterApply)
            {
                NavigationHostIncident incident = commit.Candidate.HostIncident!;
                return PublicationOutcome.Applied(new PresentationFailure(
                    PresentationFailureScope.PresentationHost,
                    NavigationPhase.Restore,
                    incident.Reason,
                    Array.Empty<PresentationReference>(),
                    hostIncident: incident));
            }

            if (kind == PresentationUpdateKind.Destination && owner.DestinationCommitBehavior == DestinationCommitBehavior.ThrowAfterApply)
            {
                throw new InvalidOperationException("The destination commit failed after it was applied.");
            }

            if (kind == PresentationUpdateKind.Destination && owner.DestinationCommitBehavior == DestinationCommitBehavior.ReturnConflictAfterApply)
            {
                return PublicationOutcome.Conflict;
            }

            if (kind == PresentationUpdateKind.Destination && owner.DestinationFailureScope.HasValue)
            {
                NavigationEntry entry = update.Changes.Single(change => change.BeforeEntry is null && change.AfterEntry is not null).AfterEntry!;
                PresentationState presentation = commit.Candidate.GetPresentation(entry.Id);
                const string reason = "The synchronous presentation publication failed.";
                PresentationFailure failure = owner.DestinationFailureScope.Value == PresentationFailureScope.PresentationHost
                    ? new PresentationFailure(
                        PresentationFailureScope.PresentationHost,
                        NavigationPhase.Commit,
                        reason,
                        Array.Empty<PresentationReference>(),
                        hostIncident: new NavigationHostIncident(
                            new NavigationIncidentId(Guid.NewGuid()),
                            reason,
                            Array.Empty<PresentationReference>()))
                    : new PresentationFailure(
                        PresentationFailureScope.CurrentPresentations,
                        NavigationPhase.Commit,
                        reason,
                        new[] { new PresentationReference(entry.Id, presentation.Id!.Value) });
                return PublicationOutcome.Applied(failure);
            }

            return PublicationOutcome.Applied();
        }

        public async ValueTask<PresentationCompletion> CompleteAsync (CancellationToken shutdownToken)
        {
            if (kind == PresentationUpdateKind.Departure && owner.FailDepartureCompletion)
            {
                PresentationFailure failure = new(PresentationFailureScope.RetiredResources, NavigationPhase.Complete, "The required departure termination failed.", Array.Empty<PresentationReference>());
                return new PresentationCompletion(new[] { failure });
            }

            if (Interlocked.Exchange(ref owner.blockNextCompletion, 0) != 0)
            {
                owner.blockedCompletion!.TrySetResult(true);
                using CancellationTokenRegistration shutdownRegistration = shutdownToken.Register(static source => ((TaskCompletionSource<bool>)source!).TrySetResult(true), owner.shutdownObservedWhileCompletionBlocked);
                try
                {
                    if (owner.IgnoreShutdownWhileCompletionIsBlocked)
                    {
                        await owner.releaseCompletion!.Task.ConfigureAwait(false);
                    }
                    else
                    {
                        await owner.releaseCompletion!.Task.WaitAsync(shutdownToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    owner.CompletionCancellationObserved = true;
                    throw;
                }
            }

            if (kind == PresentationUpdateKind.Destination && owner.ThrowOnDestinationCompletion)
            {
                throw new InvalidOperationException("The destination completion failed.");
            }

            if (kind == PresentationUpdateKind.Destination && owner.DestinationCompletionFailure is not null)
            {
                return new PresentationCompletion(new[] { owner.DestinationCompletionFailure });
            }

            return PresentationCompletion.Succeeded;
        }

        public ValueTask DisposeAsync ()
        {
            owner.PublicationDisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}

internal enum DestinationCommitBehavior
{
    Apply,
    ThrowBeforeApply,
    ReturnAppliedBeforeApply,
    ReturnHostIncidentBeforeApply,
    ThrowAfterApply,
    ReturnConflictAfterApply,
}

internal sealed record CommittedPublication (PresentationUpdate Update, NavigationState Candidate, EffectiveComposition CandidateComposition);
