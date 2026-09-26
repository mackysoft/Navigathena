using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationHostOptionsContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task Observer_configuration_keeps_its_notification_target_after_caller_collections_change ()
    {
        RecordingObserver observer = new();
        List<INavigationCommitObserver> configuredObservers = new() { observer };
        NavigationHostOptions options = new(configuredObservers);
        configuredObservers.Clear();
        ClearIfExposedAsMutable(options.CommitObservers);
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer(), options);

        await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute()));

        Assert.Equal(1, observer.CommitCount);
    }

    [Fact]
    public void Observer_configuration_rejects_a_null_observer ()
    {
        IReadOnlyList<INavigationCommitObserver> configuredObservers = new INavigationCommitObserver[] { null! };

        Assert.ThrowsAny<Exception>(() => new NavigationHostOptions(configuredObservers));
    }

    private static void ClearIfExposedAsMutable (IReadOnlyList<INavigationCommitObserver> observers)
    {
        if (observers is ICollection<INavigationCommitObserver> collection && !collection.IsReadOnly)
        {
            collection.Clear();
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
        public int CommitCount
        {
            get; private set;
        }

        public void OnCommitted (NavigationCommit commit) => CommitCount++;
    }

    private sealed record MainRoute : Route;
}
