using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Tests;

internal sealed class TestPresentationRealizer : IPresentationRealizer
{
    private readonly List<PresentationContext> contexts = new();

    public IReadOnlyList<PresentationContext> Contexts => contexts;
    public int PrepareCount
    {
        get; private set;
    }
    public bool RejectCommit
    {
        get; set;
    }
    public Action<INavigationOperationProgressReporter>? ReportProgress
    {
        get; set;
    }

    public IPresentationTransaction Begin (PresentationTransition transition, INavigationOperationProgressReporter progress)
    {
        ReportProgress?.Invoke(progress);
        return new Transaction(this);
    }

    private sealed class Transaction : IPresentationTransaction
    {
        private readonly TestPresentationRealizer owner;

        public Transaction (TestPresentationRealizer owner)
        {
            this.owner = owner;
        }

        public IReadOnlyList<NavigationEntryId> DepartureEntries { get; } = Array.Empty<NavigationEntryId>();

        public ValueTask DisposeAsync ()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<IPreparedPublication> PrepareAsync (PresentationUpdate update, CancellationToken cancellationToken)
        {
            owner.PrepareCount++;
            owner.contexts.AddRange(update.Changes.Where(change => change.Context is not null).Select(change => change.Context!));
            return new ValueTask<IPreparedPublication>(new Publication(owner));
        }
    }

    private sealed class Publication : IPreparedPublication
    {
        private readonly TestPresentationRealizer owner;

        public Publication (TestPresentationRealizer owner)
        {
            this.owner = owner;
        }

        public ValueTask<PublicationOutcome> CommitAsync (INavigationCommit commit, CancellationToken shutdownToken)
        {
            if (owner.RejectCommit)
            {
                return new ValueTask<PublicationOutcome>(PublicationOutcome.Conflict);
            }

            commit.Apply(Array.Empty<PresentationStateCapture>());
            return new ValueTask<PublicationOutcome>(PublicationOutcome.Applied());
        }

        public ValueTask<PresentationCompletion> CompleteAsync (CancellationToken shutdownToken)
        {
            return new ValueTask<PresentationCompletion>(PresentationCompletion.Succeeded);
        }

        public ValueTask DisposeAsync ()
        {
            return ValueTask.CompletedTask;
        }
    }
}
