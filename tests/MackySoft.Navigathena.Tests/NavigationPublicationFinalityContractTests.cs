using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationPublicationFinalityContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task A_destination_commit_exception_after_apply_keeps_the_committed_state_and_notifies_its_final_revision_once ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            DestinationCommitBehavior = DestinationCommitBehavior.ThrowAfterApply
        };
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));

        AssertCommittedHostFailure(result, host, NavigationPhase.Commit);
        NavigationCommit commit = Assert.Single(observer.Commits);
        Assert.Equal(result.FinalSnapshot.Revision, commit.Revision);
        AssertEquivalentState(result.FinalSnapshot, commit.State);
        AssertExpiredCapabilityCannotPublish(host, realizer);
    }

    [Fact]
    public async Task An_invalid_disposition_after_apply_keeps_the_committed_state_and_notifies_its_final_revision_once ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            DestinationCommitBehavior = DestinationCommitBehavior.ReturnConflictAfterApply
        };
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));

        AssertCommittedHostFailure(result, host, NavigationPhase.Commit);
        NavigationCommit commit = Assert.Single(observer.Commits);
        Assert.Equal(result.FinalSnapshot.Revision, commit.Revision);
        AssertEquivalentState(result.FinalSnapshot, commit.State);
        AssertExpiredCapabilityCannotPublish(host, realizer);
    }

    [Fact]
    public async Task A_destination_completion_exception_keeps_the_committed_state_without_repeating_the_logical_notification ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            ThrowOnDestinationCompletion = true
        };
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));

        AssertCommittedHostFailure(result, host, NavigationPhase.Complete);
        Assert.Single(observer.Commits);
        AssertExpiredCapabilityCannotPublish(host, realizer);
    }

    [Fact]
    public async Task A_completion_failure_racing_shutdown_keeps_the_committed_state_and_its_failure_snapshot ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            DestinationCompletionFailure = CreateHostFailure(NavigationPhase.Complete, "The destination completion could not finish."),
            IgnoreShutdownWhileCompletionIsBlocked = true,
        };
        NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        Task blockedCompletion = realizer.BlockNextCompletionAsync();

        Task<NavigationResult> operation = host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        await blockedCompletion;
        Task shutdown = host.DisposeAsync().AsTask();
        await realizer.WaitForShutdownWhileCompletionIsBlockedAsync();
        Assert.False(shutdown.IsCompleted);
        realizer.ReleaseBlockedCompletion();
        await shutdown;
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await operation);

        AssertCommittedHostFailure(result, host, NavigationPhase.Complete);
        AssertExpiredCapabilityCannotPublish(host, realizer);
    }

    [Theory]
    [InlineData((int)DestinationCommitBehavior.ThrowBeforeApply)]
    [InlineData((int)DestinationCommitBehavior.ReturnAppliedBeforeApply)]
    public async Task A_destination_commit_that_ends_before_apply_does_not_publish_or_notify (int behaviorValue)
    {
        DestinationCommitBehavior behavior = (DestinationCommitBehavior)behaviorValue;
        CoordinatedPresentationRealizer realizer = new()
        {
            DestinationCommitBehavior = behavior
        };
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));

        Assert.False(result.DestinationCommitted);
        Assert.Empty(result.FinalSnapshot.GetRegion(host.Root).Entries);
        Assert.Empty(observer.Commits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Commit);
        AssertExpiredCapabilityCannotPublish(host, realizer);
    }

    [Fact]
    public async Task A_pre_apply_host_incident_keeps_the_logical_before_state_and_returns_a_noncommitted_fault ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            DestinationCommitBehavior = DestinationCommitBehavior.ReturnHostIncidentBeforeApply
        };
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));

        Assert.False(result.DestinationCommitted);
        Assert.Equal(1, result.FinalSnapshot.Revision);
        Assert.Empty(result.FinalSnapshot.GetRegion(host.Root).Entries);
        Assert.NotNull(result.FinalSnapshot.HostIncident);
        Assert.Empty(result.Changes.CreatedEntries);
        Assert.Empty(result.Changes.RemovedEntries);
        Assert.Empty(observer.Commits);
        AssertExpiredCapabilityCannotPublish(host, realizer);
    }

    private static void AssertCommittedHostFailure (NavigationException result, NavigationHost host, NavigationPhase failurePhase)
    {
        Assert.True(result.DestinationCommitted);
        Assert.Contains(result.FinalSnapshot.GetRegion(host.Root).Entries, entryId => result.FinalSnapshot.GetEntry(entryId).Route is MainRoute);
        Assert.NotNull(result.FinalSnapshot.HostIncident);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == failurePhase);
    }

    private static PresentationFailure CreateHostFailure (NavigationPhase phase, string reason)
    {
        return new PresentationFailure(
            PresentationFailureScope.PresentationHost,
            phase,
            reason,
            Array.Empty<PresentationReference>(),
            hostIncident: new NavigationHostIncident(
                new NavigationIncidentId(Guid.NewGuid()),
                reason,
                Array.Empty<PresentationReference>()));
    }

    private static void AssertExpiredCapabilityCannotPublish (NavigationHost host, CoordinatedPresentationRealizer realizer)
    {
        NavigationState before = host.State.Current;

        Assert.ThrowsAny<Exception>(() => realizer.LastDestinationCommit!.Apply(Array.Empty<PresentationStateCapture>()));
        AssertEquivalentState(before, host.State.Current);
    }

    private static void AssertEquivalentState (NavigationState expected, NavigationState actual)
    {
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.RootRegionInstanceId, actual.RootRegionInstanceId);
        Assert.Equal(expected.HostIncident, actual.HostIncident);
        Assert.Equal(expected.Regions.Count, actual.Regions.Count);
        foreach (KeyValuePair<RegionInstanceId, RegionState> item in expected.Regions)
        {
            RegionState region = actual.GetRegion(item.Key);
            Assert.Equal(item.Value.DefinitionId, region.DefinitionId);
            Assert.Equal(item.Value.OwnerEntryId, region.OwnerEntryId);
            Assert.Equal(item.Value.Entries, region.Entries);
        }

        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        foreach (KeyValuePair<NavigationEntryId, NavigationEntry> item in expected.Entries)
        {
            Assert.Equal(item.Value, actual.GetEntry(item.Key));
        }

        Assert.Equal(expected.Presentations.Count, actual.Presentations.Count);
        foreach (KeyValuePair<NavigationEntryId, PresentationState> item in expected.Presentations)
        {
            Assert.Equal(item.Value, actual.GetPresentation(item.Key));
        }
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            }));
    }

    private sealed class RecordingObserver : INavigationCommitObserver
    {
        private readonly List<NavigationCommit> commits = new();

        public IReadOnlyList<NavigationCommit> Commits => commits;

        public void OnCommitted (NavigationCommit commit) => commits.Add(commit);
    }

    private sealed record MainRoute : Route;
}
