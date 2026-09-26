using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenHistoryContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private sealed record ChapterRoute (int Chapter) : Route;
    private sealed record ChildRoute (int Value) : Route;
    private sealed record CoverRoute : Route;

    [Fact]
    public async Task Capturing_null_clears_state_saved_by_an_earlier_visit ()
    {
        ChapterPresenter presenter = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
{
    return new(presenter);
});
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ChapterRoute(1));
        presenter.Selection = 42;
        await presenter.Activities.Last().Navigation.PushAsync(new ChapterRoute(2));
        await presenter.Activities.Last().Navigation.BackAsync();
        Assert.Equal(42, presenter.Selection);
        presenter.ClearSavedSelection = true;
        await presenter.Activities.Last().Navigation.PushAsync(new ChapterRoute(2));
        Assert.True((await presenter.Activities.Last().Navigation.Back().WaitAsync()).DestinationCommitted);
        Assert.Equal(0, presenter.Selection);
    }

    [Fact]
    public async Task A_released_single_instance_is_recreated_only_after_its_previous_owner_finishes ()
    {
        TaskCompletionSource<object?> released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<ChapterPresenter> instances = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
        {
            ChapterPresenter handler = creation.Lifetime.CreateOwned(() => new ChapterPresenter { DisposalCompletion = instances.Count == 0 ? released.Task : Task.CompletedTask });
            instances.Add(handler);
            return new(handler);
        });
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<ChapterRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Reset;
    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
});
            root.AddRoute<CoverRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push;
    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRelease;
});
        });
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, screens =>
        {
            screens.RegisterScreen(screen);
            screens.RegisterScreen(new ScreenDefinition<CoverRoute>((_, _) => new(new PassiveHandler<CoverRoute>())));
        }));
        await using NavigationHost host = NavigationHost.Create(catalog);
        try
        {
            await host.StartAsync(new ChapterRoute(1));
            instances[0].Selection = 42;
            Assert.True((await instances[0].Activities.Last().Navigation.Push(new CoverRoute()).WaitAsync()).DestinationCommitted);
            long coveredRevision = host.State.Current.Revision;
            Assert.Equal(NavigationResultKind.Conflict, (await host.Client.Back(host.Root).WaitAsync()).Kind);
            Assert.Equal(coveredRevision, host.State.Current.Revision);
            Assert.Single(instances);
            released.TrySetResult(null);
            NavigationTerminationSnapshot termination = host.Terminations.Current;
            while (termination.Records.Count > 0)
            {
                termination = await host.Terminations.WaitForChangeAsync(termination.Revision);
            }
            Assert.True((await host.Client.Back(host.Root).WaitAsync()).DestinationCommitted);
            Assert.Equal(2, instances.Count);
            Assert.Equal(42, instances[1].Selection);
            Assert.Equal(1, instances[0].DisposeCount);
        }
        finally
        {
            released.TrySetResult(null);
        }
    }

    [Fact]
    public async Task A_transition_requiring_two_displays_is_rejected_before_a_single_instance_changes ()
    {
        ChapterPresenter presenter = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
{
    return new(presenter);
});
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ChapterRoute(1));
        NavigationTransition transition = new(NavigationTransitionScope.Region, (_, _) => throw new InvalidOperationException("The effect must not be constructed."), requiresSimultaneousScreens: true);
        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await presenter.Activities.Last().Navigation.ReplaceAsync(new ChapterRoute(2), new NavigationOptions { Transition = transition }));

        Assert.Equal(1, presenter.Route!.Chapter);
        Assert.Equal(0, presenter.DeactivateCount);
        Assert.Single(presenter.Prepared);
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single, 0)]
    [InlineData(ScreenInstancePolicy.Multiple, 2)]
    public async Task A_shared_definition_enforces_instance_count_across_regions_and_route_types (ScreenInstancePolicy policy, int expectedCreations)
    {
        RegionDefinitionId left = new("left");
        RegionDefinitionId right = new("right");
        int creations = 0;
        ScreenDefinition<Route> shared = new((_, _) =>
{
    creations++;
    return new(new PassiveHandler<Route>());
}, policy);
        ScreenDefinition<ChapterRoute> parent = new((_, _) => new(new PassiveHandler<ChapterRoute>()));
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<ChapterRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            route.AddChildRegion(left, RegionCompositionMode.Exclusive, RegionOccupancy.Required, child => child.AddRoute<ChapterRoute>(Configure));
            route.AddChildRegion(right, RegionCompositionMode.Exclusive, RegionOccupancy.Required, child => child.AddRoute<ChildRoute>(Configure));
        }));
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
        {
            builder.RegisterScreens(Root, screens => screens.RegisterScreen(parent));
            builder.RegisterScreens(left, screens => screens.RegisterScreen<ChapterRoute>(shared));
            builder.RegisterScreens(right, screens => screens.RegisterScreen<ChildRoute>(shared));
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        NavigationDestinationTree<ChapterRoute> destination = Destination.For(new ChapterRoute(0)).Child(left, Destination.For(new ChapterRoute(1))).Child(right, Destination.For(new ChildRoute(2)));
        if (policy == ScreenInstancePolicy.Single)
        {
            await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await host.StartAsync(destination));
            Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
        }
        else
        {
            Assert.True((await host.Start(destination).WaitAsync()).DestinationCommitted);
        }
        Assert.Equal(expectedCreations, creations);
        static void Configure<TRoute> (RouteDefinitionBuilder<TRoute> route) where TRoute : Route
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
        }
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single)]
    [InlineData(ScreenInstancePolicy.Multiple)]
    public async Task Rebinding_parent_releases_old_child_instances_but_restores_their_history_state (ScreenInstancePolicy childPolicy)
    {
        RegionDefinitionId childRegion = new("child");
        ChapterPresenter parent = new();
        List<ChildPresenter> children = new();
        ScreenDefinition<ChapterRoute> parentScreen = new((creation, _) =>
{
    return new(parent);
});
        ScreenDefinition<ChildRoute> childScreen = new((creation, _) =>
        {
            ChildPresenter child = new();
            children.Add(child);
            return new(child);
        }, childPolicy);
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<ChapterRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            route.AddChildRegion(childRegion, RegionCompositionMode.Exclusive, RegionOccupancy.Required, child => child.AddRoute<ChildRoute>(childRoute =>
            {
                childRoute.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Replace;
                childRoute.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            }));
        }));
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
        {
            builder.RegisterScreens(Root, screens => screens.RegisterScreen(parentScreen));
            builder.RegisterScreens(childRegion, screens => screens.RegisterScreen(childScreen));
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        Assert.Equal(NavigationResultKind.Committed, (await host.Start(Destination.For(new ChapterRoute(1)).Child(childRegion, Destination.For(new ChildRoute(11)))).WaitAsync()).Kind);
        children[0].Selection = 42;
        Assert.Equal(NavigationResultKind.Committed, (await parent.Activities.Last().Navigation.Push(Destination.For(new ChapterRoute(2)).Child(childRegion, Destination.For(new ChildRoute(22)))).WaitAsync()).Kind);
        Assert.Equal(1, children[0].Terminations);
        Assert.Equal(NavigationResultKind.Committed, (await parent.Activities.Last().Navigation.Back().WaitAsync()).Kind);
        Assert.Equal(3, children.Count);
        Assert.Equal(11, children[2].Route!.Value);
        Assert.Equal(42, children[2].Selection);
        Assert.Equal(1, parent.InitializeCount);
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single, 1)]
    [InlineData(ScreenInstancePolicy.Multiple, 2)]
    public async Task Push_and_back_deliver_history_input_without_changing_instance_ownership (ScreenInstancePolicy policy, int expectedInstances)
    {
        List<ChapterPresenter> instances = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
        {
            ChapterPresenter presenter = creation.Lifetime.CreateOwned(() => new ChapterPresenter());
            instances.Add(presenter);
            return new(presenter);
        }, policy);
        await using NavigationHost host = CreateHost(screen);
        Assert.Equal(NavigationResultKind.Committed, (await host.Start(new ChapterRoute(1)).WaitAsync()).Kind);
        ChapterPresenter first = instances[0];
        first.Selection = 42;
        ScreenActivityContext original = first.Activities.Single();
        Assert.True(original.IsFirstActivation);
        Assert.Equal(ScreenPreparationReason.NewEntry, original.PreparationReason);
        Assert.Equal(NavigationResultKind.Committed, (await original.Navigation.Push(new ChapterRoute(2)).WaitAsync()).Kind);
        Assert.Equal(2, host.State.Current.GetRegion(host.Root).Entries.Count);
        Assert.Equal(expectedInstances, instances.Count);
        Assert.Equal(NavigationResultKind.Rejected, (await original.Navigation.Back().WaitAsync()).Kind);
        ChapterPresenter current = instances.Last();
        Assert.True(current.Activities.Last().IsFirstActivation);
        Assert.Equal(2, current.Route!.Chapter);
        Assert.Equal(NavigationResultKind.Committed, (await current.Activities.Last().Navigation.Back().WaitAsync()).Kind);
        Assert.Single(host.State.Current.GetRegion(host.Root).Entries);
        Assert.Equal(1, first.Route!.Chapter);
        Assert.Equal(42, first.Selection);
        Assert.Equal(policy == ScreenInstancePolicy.Single ? 3 : 1, first.Prepared.Count);
        Assert.Equal(1, first.InitializeCount);
        Assert.False(first.Activities.Last().IsFirstActivation);
        Assert.Equal(ScreenActivationReason.HistoryReturn, first.Activities.Last().Reason);
        Assert.Equal(policy == ScreenInstancePolicy.Single ? ScreenPreparationReason.HistoryRestoration : null, first.Activities.Last().PreparationReason);
        await host.ShutdownAsync();
        Assert.All(instances, presenter =>
{
    Assert.Equal(1, presenter.TerminateCount);
    Assert.Equal(1, presenter.DisposeCount);
});
    }

    [Fact]
    public async Task Replace_changes_only_the_current_history_entry_and_preserves_single_instance ()
    {
        ChapterPresenter presenter = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
{
    return new(creation.Lifetime.CreateOwned(() => presenter));
});
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ChapterRoute(1));
        NavigationEntryId first = host.State.Current.GetRegion(host.Root).Entries.Single();
        NavigationResult replaced = await presenter.Activities.Last().Navigation.Replace(new ChapterRoute(3)).WaitAsync();
        Assert.Equal(NavigationResultKind.Committed, replaced.Kind);
        Assert.NotEqual(first, host.State.Current.GetRegion(host.Root).Entries.Single());
        Assert.Equal(3, presenter.Route!.Chapter);
        Assert.Equal(1, presenter.InitializeCount);
    }

    [Fact]
    public async Task Single_instance_simultaneous_display_is_rejected_before_stopping_or_constructing ()
    {
        ChapterPresenter presenter = new();
        int creations = 0;
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
{
    creations++;
    return new(presenter);
});
        await using NavigationHost host = CreateHost(screen, LowerPresentationPolicy.Preserve);
        await host.StartAsync(new ChapterRoute(1));
        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await presenter.Activities.Last().Navigation.PushAsync(new ChapterRoute(2)));

        Assert.Single(host.State.Current.GetRegion(host.Root).Entries);
        Assert.Equal(1, creations);
        Assert.Equal(0, presenter.DeactivateCount);
        Assert.False(presenter.Activities.Single().CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Failed_single_input_restores_old_route_and_saved_state_before_reopening_activity ()
    {
        ChapterPresenter presenter = new()
        {
            FailChapter = 2
        };
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
{
    return new(presenter);
});
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ChapterRoute(1));
        presenter.Selection = 42;
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await presenter.Activities.Last().Navigation.PushAsync(new ChapterRoute(2)));
        Assert.False(result.DestinationCommitted);
        Assert.Single(host.State.Current.GetRegion(host.Root).Entries);
        Assert.Equal(1, presenter.Route!.Chapter);
        Assert.Equal(42, presenter.Selection);
        Assert.Equal(1, presenter.InitializeCount);
    }

    private static NavigationHost CreateHost (ScreenDefinition<ChapterRoute> screen, LowerPresentationPolicy? lower = null)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<ChapterRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = lower ?? LowerPresentationPolicy.HideAndRetain;
        }));
        return NavigationHost.Create(ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(Root, screens => screens.RegisterScreen(screen))));
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single, false)]
    [InlineData(ScreenInstancePolicy.Single, true)]
    [InlineData(ScreenInstancePolicy.Multiple, false)]
    [InlineData(ScreenInstancePolicy.Multiple, true)]
    public async Task Reload_recreates_owned_instances_without_changing_the_visit_or_route (ScreenInstancePolicy policy, bool restoreState)
    {
        List<ChapterPresenter> instances = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
        {
            if (policy == ScreenInstancePolicy.Single && instances.Count > 0)
            {
                Assert.Equal(1, instances[^1].DisposeCount);
            }
            ChapterPresenter presenter = creation.Lifetime.CreateOwned(() => new ChapterPresenter());
            instances.Add(presenter);
            return new(presenter);
        }, policy);
        ScreenCatalog catalog = ScreenCatalog.Build(screens => screens.Register(screen, route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
        }));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new ChapterRoute(7));
        ChapterPresenter old = instances.Single();
        ScreenActivityContext activity = old.Activities.Single();
        old.Selection = 42;
        await activity.Navigation.ReloadAsync(new ReloadOptions { RestoreState = restoreState });
        ChapterPresenter current = instances.Last();
        Assert.Equal(2, instances.Count);
        Assert.Equal(1, old.TerminateCount);
        Assert.Equal(1, old.DisposeCount);
        Assert.Equal(activity.EntryId, Assert.Single(host.State.Current.GetRegion(host.Root).Entries));
        Assert.Equal(new ChapterRoute(7), current.Route);
        Assert.Equal(restoreState ? 42 : 0, current.Selection);
        Assert.Equal(ScreenActivationReason.Reload, current.Activities.Single().Reason);
        Assert.Equal(ScreenPreparationReason.Reload, current.Activities.Single().PreparationReason);
        Assert.False(current.Activities.Single().IsFirstActivation);
        await Assert.ThrowsAsync<NavigationException>(() => activity.Navigation.BackAsync());
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single)]
    [InlineData(ScreenInstancePolicy.Multiple)]
    public async Task Failed_reload_restores_the_same_history_entry_and_saved_display (ScreenInstancePolicy policy)
    {
        int attempts = 0;
        List<ChapterPresenter> instances = new();
        ScreenDefinition<ChapterRoute> screen = new((creation, _) =>
        {
            if (++attempts == 2)
            {
                throw new InvalidOperationException("Loading failed.");
            }
            ChapterPresenter presenter = creation.Lifetime.CreateOwned(() => new ChapterPresenter());
            instances.Add(presenter);
            return new(presenter);
        }, policy);
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ChapterRoute(7));
        ChapterPresenter old = instances.Single();
        old.Selection = 42;
        NavigationEntryId entry = old.Activities.Single().EntryId;
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => old.Activities.Single().Navigation.ReloadAsync());
        Assert.False(failure.DestinationCommitted);
        Assert.Equal(entry, Assert.Single(host.State.Current.GetRegion(host.Root).Entries));
        ChapterPresenter restored = instances.Last();
        Assert.Equal(new ChapterRoute(7), restored.Route);
        Assert.Equal(42, restored.Selection);
        Assert.False(restored.Activities.Last().CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Reloading_a_parent_preserves_child_history_but_rebuilds_the_child_from_the_new_parent ()
    {
        RegionDefinitionId childRegion = new("child");
        List<ChapterPresenter> parents = new();
        List<ChildPresenter> children = new();
        List<ChapterPresenter> childOwners = new();
        ScreenCatalog catalog = ScreenCatalog.Build(screens => screens.Register<ChapterRoute>((creation, _) =>
        {
            ChapterPresenter parent = creation.Lifetime.CreateOwned(() => new ChapterPresenter());
            parents.Add(parent);
            creation.RegisterScreens(childRegion, childScreens => childScreens.RegisterScreen(new ScreenDefinition<ChildRoute>((_, _) =>
            {
                ChildPresenter child = new();
                children.Add(child);
                childOwners.Add(parent);
                return new(child);
            })));
            return new(parent);
        }, route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            route.AddChildRegion(childRegion, RegionCompositionMode.Exclusive, RegionOccupancy.Required, child => child.AddRoute<ChildRoute>(childRoute =>
            {
                childRoute.AllowedEntryOperations = RouteEntryOperations.Replace;
                childRoute.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            }));
        }));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new ChapterRoute(7)).Child(childRegion, Destination.For(new ChildRoute(3))));
        NavigationEntryId[] history = host.State.Current.Entries.Keys.ToArray();
        children[0].Selection = 42;
        await parents[0].Activities.Last().Navigation.ReloadAsync(new ReloadOptions { RestoreState = true });
        Assert.Equal(history.OrderBy(entry => entry.Value), host.State.Current.Entries.Keys.OrderBy(entry => entry.Value));
        Assert.Equal(2, parents.Count);
        Assert.Equal(2, children.Count);
        Assert.Same(parents[1], childOwners[1]);
        Assert.Equal(1, children[0].Terminations);
        Assert.Equal(42, children[1].Selection);
        Assert.Equal(new ChildRoute(3), children[1].Route);
    }

    [Fact]
    public async Task Combined_catalog_registration_can_bind_child_construction_before_route_definitions_are_built ()
    {
        RegionDefinitionId childRegion = new("child");
        ChapterPresenter parent = new();
        ChildPresenter child = new();
        int creations = 0;
        ScreenCatalog catalog = ScreenCatalog.Build(screens =>
        {
            screens.RegisterScreens(childRegion, registrations => registrations.RegisterScreen(new ScreenDefinition<ChildRoute>((_, _) =>
            {
                creations++;
                return new(child);
            })));
            screens.Register<ChapterRoute>((_, _) => new(parent), route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                route.AddChildRegion(childRegion, RegionCompositionMode.Exclusive, RegionOccupancy.Required, childRoutes => childRoutes.AddRoute<ChildRoute>(childRoute =>
                {
                    childRoute.AllowedEntryOperations = RouteEntryOperations.Replace;
                    childRoute.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                }));
            });
        });
        Assert.Equal(0, creations);
        await using NavigationHost host = NavigationHost.Create(catalog);

        await host.StartAsync(Destination.For(new ChapterRoute(7)).Child(childRegion, Destination.For(new ChildRoute(3))));

        Assert.Equal(1, creations);
        Assert.Equal(new ChapterRoute(7), parent.Route);
        Assert.Equal(new ChildRoute(3), child.Route);
    }

    private sealed class PassiveHandler<TRoute> : IScreenLifecycleHandler<TRoute> where TRoute : Route
    {
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
        public ValueTask ActivateAsync (TRoute route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
    }

    private sealed class ChapterPresenter : IScreenLifecycleHandler<ChapterRoute>, IScreenStateCapture, IAsyncDisposable
    {
        public ChapterRoute? Route
        {
            get; private set;
        }
        public int Selection
        {
            get; set;
        }
        public int? FailChapter
        {
            get; init;
        }
        public Task DisposalCompletion { get; init; } = Task.CompletedTask;
        public bool ClearSavedSelection
        {
            get; set;
        }
        public int InitializeCount
        {
            get; private set;
        }
        public int DeactivateCount
        {
            get; private set;
        }
        public int TerminateCount
        {
            get; private set;
        }
        public int DisposeCount
        {
            get; private set;
        }
        public List<ChapterRoute> Prepared { get; } = new();
        public List<ScreenActivityContext> Activities { get; } = new();
        public object? CaptureState () => ClearSavedSelection ? null : Selection;
        public ValueTask InitializeAsync (CancellationToken cancellationToken)
        {
            InitializeCount++;
            return default;
        }
        public ValueTask PrepareAsync (ChapterRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Route = route;
            Selection = preparation.SavedState is int saved ? saved : 0;
            Prepared.Add(route);
            if (route.Chapter == FailChapter)
            {
                throw new InvalidOperationException("Chapter preparation failed after changing displayed data.");
            }
            return default;
        }
        public ValueTask ActivateAsync (ChapterRoute route, ScreenActivityContext activity)
        {
            Route = route;
            Activities.Add(activity);
            return default;
        }
        public ValueTask DeactivateAsync ()
        {
            DeactivateCount++;
            return default;
        }
        public ValueTask TerminateAsync ()
        {
            TerminateCount++;
            return default;
        }
        public async ValueTask DisposeAsync ()
        {
            DisposeCount++;
            await DisposalCompletion;
        }
    }

    private sealed class ChildPresenter : IScreenLifecycleHandler<ChildRoute>, IScreenStateCapture
    {
        public ChildRoute? Route
        {
            get; private set;
        }
        public int Selection
        {
            get; set;
        }
        public int Terminations
        {
            get; private set;
        }
        public object CaptureState () => Selection;
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (ChildRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Route = route;
            Selection = preparation.SavedState is int saved ? saved : 0;
            return default;
        }
        public ValueTask ActivateAsync (ChildRoute route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync ()
        {
            Terminations++;
            return default;
        }
    }
}
