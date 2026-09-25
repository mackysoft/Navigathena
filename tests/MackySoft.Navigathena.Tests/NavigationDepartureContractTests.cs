using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationDepartureContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task Failed_required_departure_restores_the_original_history_without_preparing_the_destination ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult initial = await host.Client.Reset(host.Root, Destination.For(new HomeRoute())).WaitAsync();
        NavigationEntry home = initial.FinalSnapshot.GetEntry(initial.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        PresentationId originalPresentation = initial.FinalSnapshot.GetPresentation(home.Id).Id!.Value;
        PresentationContext homeContext = Assert.Single(realizer.Contexts, context => context.EntryId == home.Id);
        IScreenNavigation staleAction = homeContext.Navigation.GetRegionNavigation(RegionTarget.Root);
        int updatesBeforeFailure = realizer.UpdateKinds.Count;
        realizer.DepartAllowedEntries = true;
        realizer.FailDepartureCompletion = true;

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new PlayRoute())));
        PresentationUpdateKind[] failurePath = realizer.UpdateKinds.Skip(updatesBeforeFailure).ToArray();

        Assert.False(result.DestinationCommitted);
        Assert.Equal(RestorationOutcome.Restored, result.Restoration);
        Assert.Contains(PresentationUpdateKind.Departure, failurePath);
        Assert.Contains(PresentationUpdateKind.Restoration, failurePath);
        Assert.DoesNotContain(PresentationUpdateKind.Destination, failurePath);
        Assert.IsType<HomeRoute>(result.FinalSnapshot.GetEntry(result.FinalSnapshot.GetRegion(host.Root).Entries.Single()).Route);
        Assert.IsType<HomeRoute>(initial.FinalSnapshot.GetEntry(initial.FinalSnapshot.GetRegion(host.Root).Entries.Single()).Route);
        Assert.NotEqual(originalPresentation, result.FinalSnapshot.GetPresentation(home.Id).Id);

        NavigationResult stale = await staleAction.Reset(Destination.For(new HomeRoute())).WaitAsync();

        Assert.Equal(NavigationResultKind.Rejected, stale.Kind);
        Assert.False(stale.DestinationCommitted);
    }

    [Fact]
    public async Task Failed_restoration_marks_the_departed_source_as_lost_and_reports_the_restore_failure ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            DepartAllowedEntries = true,
            FailDepartureCompletion = true,
            RejectRestorationCommit = true,
        };
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult initial = await host.Client.Reset(host.Root, Destination.For(new HomeRoute())).WaitAsync();
        NavigationEntry home = initial.FinalSnapshot.GetEntry(initial.FinalSnapshot.GetRegion(host.Root).Entries.Single());

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new PlayRoute())));

        Assert.False(result.DestinationCommitted);
        Assert.Equal(RestorationOutcome.Failed, result.Restoration);
        Assert.Equal(PresentationMaterialization.Lost, result.FinalSnapshot.GetPresentation(home.Id).Materialization);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Restore);
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

    private sealed record HomeRoute : Route;
    private sealed record PlayRoute : Route;
}
