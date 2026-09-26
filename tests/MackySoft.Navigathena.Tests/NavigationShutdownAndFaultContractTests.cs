using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationShutdownAndFaultContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task Shutdown_completes_queued_navigation_recovery_and_loss_requests_against_the_last_host_snapshot ()
    {
        BlockingObserver observer = new();
        CoordinatedPresentationRealizer realizer = new();
        NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        NavigationResult initialOverlay = await host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        NavigationEntry overlay = Assert.Single(initialOverlay.Changes.CreatedEntries);
        NavigationState beforeShutdown = host.State.Current;
        observer.BlockNextCommit();

        Task<NavigationResult> committedOperation = host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        await observer.WaitForBlockAsync();
        NavigationState lastSnapshot = host.State.Current;
        Task<NavigationResult> queuedNavigation = host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        Task<NavigationResult> queuedRecovery = host.Recovery.RecoverAsync(new NavigationIncidentId(Guid.NewGuid())).AsTask();
        Task<PresentationLossResult> queuedLoss = host.Loss.ReportAsync(
            new PresentationLoss(
                new[]
                {
                    new PresentationReference(main.Id, beforeShutdown.GetPresentation(main.Id).Id!.Value),
                    new PresentationReference(overlay.Id, beforeShutdown.GetPresentation(overlay.Id).Id!.Value),
                },
                "The host is closing.")).AsTask();
        Task shutdown = host.DisposeAsync().AsTask();

        observer.Release();
        await shutdown;

        NavigationResult committed = await committedOperation;
        Assert.True(committed.DestinationCommitted);
        Assert.Contains(committed.FinalSnapshot.GetRegion(host.Root).Entries, entryId => committed.FinalSnapshot.GetEntry(entryId).Route is OverlayRoute);
        Assert.Equal(NavigationResultKind.Conflict, (await queuedNavigation).Kind);
        NavigationException closed = await Assert.ThrowsAsync<NavigationException>(() => queuedRecovery);
        Assert.IsType<NavigationRuntimeClosedException>(closed.InnerException);
        Assert.Same(lastSnapshot, closed.FinalSnapshot);
        Assert.Equal(PresentationLossResult.RuntimeClosed, await queuedLoss);
        Assert.Equal(PresentationMaterialization.Available, host.State.Current.GetPresentation(main.Id).Materialization);
        Assert.Equal(PresentationMaterialization.Available, host.State.Current.GetPresentation(overlay.Id).Materialization);
    }

    [Fact]
    public async Task Shutdown_cancels_a_preparing_transaction_and_waits_for_its_adapter_ownership_to_end ()
    {
        CoordinatedPresentationRealizer realizer = new();
        NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        Task blockedPreparation = realizer.BlockNextPreparationAsync();

        Task<NavigationResult> operation = host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        await blockedPreparation;
        await host.DisposeAsync();
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await operation);

        Assert.True(realizer.PreparationCancellationObserved);
        Assert.False(result.DestinationCommitted);
        Assert.Empty(result.FinalSnapshot.GetRegion(host.Root).Entries);
    }

    [Fact]
    public async Task Shutdown_after_apply_keeps_the_committed_state_and_waits_for_publication_termination ()
    {
        CoordinatedPresentationRealizer realizer = new();
        NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        Task blockedCompletion = realizer.BlockNextCompletionAsync();

        Task<NavigationResult> operation = host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        await blockedCompletion;
        await host.DisposeAsync();
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await operation);

        Assert.True(realizer.CompletionCancellationObserved);
        Assert.True(result.DestinationCommitted);
        Assert.Contains(result.FinalSnapshot.GetRegion(host.Root).Entries, entryId => result.FinalSnapshot.GetEntry(entryId).Route is OverlayRoute);
    }

    [Theory]
    [InlineData(PresentationFailureScope.CurrentPresentations)]
    [InlineData(PresentationFailureScope.PresentationHost)]
    public async Task A_faulted_commit_notifies_observers_with_the_revision_and_state_that_include_its_failure (PresentationFailureScope failureScope)
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            DestinationFailureScope = failureScope
        };
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));
        NavigationCommit commit = Assert.Single(observer.Commits);
        NavigationEntry main = result.FinalSnapshot.GetEntry(result.FinalSnapshot.GetRegion(host.Root).Entries.Single());

        Assert.Equal(commit.Revision, commit.State.Revision);
        Assert.Equal(result.FinalSnapshot.Revision, commit.State.Revision);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Commit);
        if (failureScope == PresentationFailureScope.CurrentPresentations)
        {
            Assert.Equal(PresentationMaterialization.Lost, commit.State.GetPresentation(main.Id).Materialization);
            Assert.Null(commit.State.HostIncident);
        }
        else
        {
            Assert.NotNull(commit.State.HostIncident);
            Assert.Equal(commit.State.HostIncident, result.FinalSnapshot.HostIncident);
        }
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            root.AddRoute<OverlayRoute>(overlay =>
            {
                overlay.AllowedEntryOperations = RouteEntryOperations.Push;
                overlay.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
        });
    }

    private sealed class BlockingObserver : INavigationCommitObserver
    {
        private TaskCompletionSource<bool>? blocked;
        private TaskCompletionSource<bool>? release;

        public void BlockNextCommit ()
        {
            blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForBlockAsync () => blocked!.Task;

        public void Release () => release!.TrySetResult(true);

        public void OnCommitted (NavigationCommit commit)
        {
            if (blocked is null || release is null)
            {
                return;
            }

            blocked.TrySetResult(true);
            release.Task.GetAwaiter().GetResult();
            blocked = null;
            release = null;
        }
    }

    private sealed class RecordingObserver : INavigationCommitObserver
    {
        private readonly List<NavigationCommit> commits = new();

        public IReadOnlyList<NavigationCommit> Commits => commits;

        public void OnCommitted (NavigationCommit commit) => commits.Add(commit);
    }

    private sealed record MainRoute : Route;
    private sealed record OverlayRoute : Route;
}
