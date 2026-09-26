using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationCallerCancellationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task Caller_cancellation_after_destination_preparation_and_before_apply_does_not_commit_or_notify_the_destination ()
    {
        CoordinatedPresentationRealizer realizer = new();
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));
        Task destinationApplyBlocked = realizer.BlockNextDestinationApplyAsync();

        NavigationOperation operation = host.Client.Reset(host.Root, Destination.For(new HomeRoute()));
        await destinationApplyBlocked;
        Assert.True(operation.TryRequestCancellation());
        realizer.ReleaseBlockedDestinationApply();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync());
        Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
        Assert.Empty(observer.Commits);
    }

    [Fact]
    public async Task Cancellation_is_closed_after_departure_crosses_the_irreversible_boundary ()
    {
        CoordinatedPresentationRealizer realizer = new();
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));
        await host.Client.ResetAsync(host.Root, Destination.For(new HomeRoute()));
        realizer.DepartAllowedEntries = true;
        Task destinationApplyBlocked = realizer.BlockNextDestinationApplyAsync();

        NavigationOperation operation = host.Client.Reset(host.Root, Destination.For(new PlayRoute()));
        await destinationApplyBlocked;
        Assert.False(operation.TryRequestCancellation());
        realizer.ReleaseBlockedDestinationApply();
        NavigationResult result = await operation.WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, result.Kind);
        Assert.True(result.DestinationCommitted);
        Assert.Equal(RestorationOutcome.NotRequired, result.Restoration);
        NavigationEntry destination = result.FinalSnapshot.GetEntry(Assert.Single(result.FinalSnapshot.GetRegion(host.Root).Entries));
        Assert.IsType<PlayRoute>(destination.Route);
        Assert.Equal(2, observer.Commits.Count);
    }

    [Fact]
    public async Task Async_operation_cancellation_cancels_the_transition_and_waits_for_it_to_settle ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        using CancellationTokenSource cancellation = new();
        Task blocked = realizer.BlockNextDestinationApplyAsync();
        Task transition = host.Client.ResetAsync(host.Root, Destination.For(new HomeRoute()), cancellationToken: cancellation.Token);
        await blocked;
        cancellation.Cancel();
        Assert.False(transition.IsCompleted);
        realizer.ReleaseBlockedDestinationApply();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transition);
        Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
    }

    [Fact]
    public async Task Async_operation_cancellation_after_irreversible_departure_does_not_abandon_the_transition ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        await host.Client.ResetAsync(host.Root, Destination.For(new HomeRoute()));
        realizer.DepartAllowedEntries = true;
        using CancellationTokenSource cancellation = new();
        Task blocked = realizer.BlockNextDestinationApplyAsync();
        Task transition = host.Client.ResetAsync(host.Root, Destination.For(new PlayRoute()), cancellationToken: cancellation.Token);
        await blocked;
        cancellation.Cancel();
        Assert.False(transition.IsCompleted);
        realizer.ReleaseBlockedDestinationApply();
        await transition;
        Assert.IsType<PlayRoute>(host.State.Current.GetEntry(Assert.Single(host.State.Current.GetRegion(host.Root).Entries)).Route);
    }

    [Fact]
    public async Task Awaiting_a_rejected_back_throws_instead_of_continuing_as_success ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new CoordinatedPresentationRealizer());
        await host.Client.ResetAsync(host.Root, Destination.For(new HomeRoute()));
        await Assert.ThrowsAsync<NavigationException>(() => host.Client.BackAsync(host.Root));
        Assert.IsType<HomeRoute>(host.State.Current.GetEntry(Assert.Single(host.State.Current.GetRegion(host.Root).Entries)).Route);
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Exclusive, root =>
        {
            root.AddRoute<HomeRoute>(home =>
            {
                home.AllowedEntryOperations = RouteEntryOperations.Reset;
                home.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            root.AddRoute<PlayRoute>(play =>
            {
                play.AllowedEntryOperations = RouteEntryOperations.Reset;
                play.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRelease;
            });
        });
    }

    private sealed class RecordingObserver : INavigationCommitObserver
    {
        private readonly List<NavigationCommit> commits = new();

        public IReadOnlyList<NavigationCommit> Commits => commits;

        public void OnCommitted (NavigationCommit commit) => commits.Add(commit);
    }

    private sealed record HomeRoute : Route;
    private sealed record PlayRoute : Route;
}
