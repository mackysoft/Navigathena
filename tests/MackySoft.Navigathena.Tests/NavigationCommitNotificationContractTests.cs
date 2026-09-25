using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationCommitNotificationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Left = new("left");
    private static readonly RegionDefinitionId Right = new("right");

    [Fact]
    public async Task A_later_independent_commit_notifies_in_revision_order_without_waiting_for_earlier_completion ()
    {
        CoordinatedPresentationRealizer realizer = new();
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId left = FindChildRegion(reset.FinalSnapshot, main.Id, Left);
        RegionInstanceId right = FindChildRegion(reset.FinalSnapshot, main.Id, Right);
        Task blockedCompletion = realizer.BlockNextCompletionAsync();

        Task<NavigationResult> leftOperation = host.Client.Push(left, Destination.For(new LeftRoute())).WaitAsync();
        await blockedCompletion;
        NavigationResult rightResult = await host.Client.Push(right, Destination.For(new RightRoute())).WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, rightResult.Kind);
        Assert.Equal(new long[] { 1, 2, 3 }, observer.Commits.Select(commit => commit.Revision));
        Assert.All(observer.Commits, commit => Assert.Equal(commit.Revision, commit.State.Revision));
        Assert.Equal(3, observer.Commits.Count(commit => commit.Changes.CreatedEntries.Count > 0));

        realizer.ReleaseBlockedCompletion();
        NavigationResult leftResult = await leftOperation;

        Assert.Equal(NavigationResultKind.Committed, leftResult.Kind);
    }

    private static RegionInstanceId FindChildRegion (NavigationState state, NavigationEntryId owner, RegionDefinitionId definitionId)
    {
        return state.Regions.Values.Single(region => region.OwnerEntryId == owner && region.DefinitionId == definitionId).Id;
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Exclusive, root =>
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                main.AddChildRegion(Left, RegionCompositionMode.Layered, RegionOccupancy.Optional, left =>
                    left.AddRoute<LeftRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Push;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    }));
                main.AddChildRegion(Right, RegionCompositionMode.Layered, RegionOccupancy.Optional, right =>
                    right.AddRoute<RightRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Push;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    }));
            }));
    }

    private sealed class RecordingObserver : INavigationCommitObserver
    {
        private readonly List<NavigationCommit> commits = new();

        public IReadOnlyList<NavigationCommit> Commits => commits.AsReadOnly();

        public void OnCommitted (NavigationCommit commit)
        {
            commits.Add(commit);
        }
    }

    private sealed record MainRoute : Route;
    private sealed record LeftRoute : Route;
    private sealed record RightRoute : Route;
}
