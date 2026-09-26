using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationConcurrencyContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Left = new("left");
    private static readonly RegionDefinitionId Right = new("right");

    [Fact]
    public async Task Independent_child_operations_prepared_from_the_same_state_both_commit_when_they_commit_in_reverse_order ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId left = FindChildRegion(reset.FinalSnapshot, main.Id, Left);
        RegionInstanceId right = FindChildRegion(reset.FinalSnapshot, main.Id, Right);
        Task blockedPreparation = realizer.BlockNextPreparationAsync();

        Task<NavigationResult> leftOperation = host.Client.Push(left, Destination.For(new LeftRoute())).WaitAsync();
        await blockedPreparation;
        NavigationResult rightResult = await host.Client.Push(right, Destination.For(new RightRoute())).WaitAsync();
        realizer.ReleaseBlockedPreparation();
        NavigationResult leftResult = await leftOperation;

        Assert.Equal(NavigationResultKind.Committed, rightResult.Kind);
        Assert.Equal(NavigationResultKind.Committed, leftResult.Kind);
        NavigationEntry leftEntry = host.State.Current.GetEntry(host.State.Current.GetRegion(left).Entries.Single());
        NavigationEntry rightEntry = host.State.Current.GetEntry(host.State.Current.GetRegion(right).Entries.Single());
        Assert.IsType<LeftRoute>(leftEntry.Route);
        Assert.IsType<RightRoute>(rightEntry.Route);

        CommittedPublication leftCommit = realizer.CommittedPublications.Single(publication => publication.Candidate.Revision == leftResult.FinalSnapshot.Revision);

        Assert.Equal(new[] { main.Id, leftEntry.Id }, leftCommit.Update.ProposedComposition.Presentations.Select(presentation => presentation.EntryId));
        Assert.Equal(new[] { main.Id, leftEntry.Id, rightEntry.Id }, leftCommit.CandidateComposition.Presentations.Select(presentation => presentation.EntryId));
        Assert.Equal(leftResult.FinalSnapshot.Revision, leftCommit.Candidate.Revision);
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

    private sealed record MainRoute : Route;
    private sealed record LeftRoute : Route;
    private sealed record RightRoute : Route;
}
