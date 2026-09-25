using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationRestorationShutdownContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task Shutdown_interrupting_a_failed_departure_restoration_keeps_the_original_navigation_result ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        await host.Client.ResetAsync(host.Root, Destination.For(new HomeRoute()));
        realizer.DepartAllowedEntries = true;
        Task restorationCommitBlocked = realizer.BlockNextRestorationCommitAsync();
        realizer.DestinationCommitBehavior = DestinationCommitBehavior.ThrowBeforeApply;

        Task<NavigationResult> navigation = host.Client.Reset(host.Root, Destination.For(new PlayRoute())).WaitAsync();
        await restorationCommitBlocked;
        PresentationTransition originalTransition = realizer.Transitions[^1];

        Task shutdown = host.DisposeAsync().AsTask();
        await shutdown;
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await navigation);

        Assert.Equal(originalTransition.OperationId, result.OperationId);
        Assert.Equal(originalTransition.Operation, result.Operation);
        Assert.False(result.DestinationCommitted);
        Assert.Equal(RestorationOutcome.Failed, result.Restoration);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Restore && !string.IsNullOrWhiteSpace(diagnostic.Reason));
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
