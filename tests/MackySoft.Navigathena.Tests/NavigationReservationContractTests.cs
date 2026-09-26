using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationReservationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Child = new("child");

    [Fact]
    public async Task A_child_operation_conflicts_before_preparation_when_an_overlapping_parent_coverage_change_is_preparing ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId child = reset.FinalSnapshot.Regions.Values.Single(region => region.OwnerEntryId == main.Id && region.DefinitionId == Child).Id;
        Task blockedPreparation = realizer.BlockNextPreparationAsync();

        Task<NavigationResult> parentOperation = host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        await blockedPreparation;
        int preparationsBeforeConflict = realizer.UpdateKinds.Count;

        NavigationResult childResult = await host.Client.Push(child, Destination.For(new ChildRoute())).WaitAsync();

        Assert.Equal(NavigationResultKind.Conflict, childResult.Kind);
        Assert.False(childResult.DestinationCommitted);
        Assert.Equal(preparationsBeforeConflict, realizer.UpdateKinds.Count);

        realizer.ReleaseBlockedPreparation();
        NavigationResult parentResult = await parentOperation;

        Assert.Equal(NavigationResultKind.Committed, parentResult.Kind);
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                main.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
                    child.AddRoute<ChildRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Push;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    }));
            });
            root.AddRoute<OverlayRoute>(overlay =>
            {
                overlay.AllowedEntryOperations = RouteEntryOperations.Push;
                overlay.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            });
        });
    }

    private sealed record MainRoute : Route;
    private sealed record OverlayRoute : Route;
    private sealed record ChildRoute : Route;
}
