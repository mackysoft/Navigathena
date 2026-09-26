using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenRestartContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Hud = new("hud");
    private sealed record BootRoute : Route;
    private sealed record GameRoute (int Level) : Route;
    private sealed record PopupRoute : Route;
    private sealed record HudRoute (int Page) : Route;

    [Theory]
    [InlineData(ScreenInstancePolicy.Single)]
    [InlineData(ScreenInstancePolicy.Multiple)]
    public async Task Repeated_boot_resets_keep_the_host_and_external_resources_but_create_new_visits_and_instances (ScreenInstancePolicy policy)
    {
        ResourceLifetime lifetime = new();
        ExternalView view = new();
        ResourceReference<ExternalView> reference = lifetime.Reference(view);
        List<Handler<BootRoute>> boots = new();
        ScreenDefinition<BootRoute> boot = new(async (creation, token) =>
        {
            Assert.Same(view, await creation.Lifetime.BorrowAsync(reference, token));
            if (policy == ScreenInstancePolicy.Single && boots.Count > 0)
            {
                Assert.Equal(1, boots[^1].Disposals);
            }
            Handler<BootRoute> handler = creation.Lifetime.CreateOwned(() => new Handler<BootRoute>());
            boots.Add(handler);
            return handler;
        }, policy);
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(Root, RegionCompositionMode.Layered,
            screens => screens.Register(boot, Configure)));
        await host.StartAsync(new BootRoute());
        RegionInstanceId root = host.Root;
        ScreenActivityContext initial = boots[0].Activity!;
        HashSet<NavigationEntryId> visits = new() { initial.EntryId };
        for (int i = 0; i < 3; i++)
        {
            await boots[^1].Activity!.Navigation.GetRegionNavigation(RegionTarget.Root)
                .ResetAsync(new BootRoute(), new NavigationOptions { RecreateInstance = true });
            Assert.Equal(root, host.Root);
            Assert.True(visits.Add(boots[^1].Activity!.EntryId));
            Assert.Single(host.State.Current.Entries);
            Assert.Equal(0, view.Disposals);
        }
        await Assert.ThrowsAsync<NavigationException>(() => initial.Navigation.ResetAsync(new BootRoute()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(new BootRoute()));
        Assert.Equal(4, boots.Count);
        await host.ShutdownAsync();
        await lifetime.EndAsync();
        Assert.All(boots, handler => Assert.Equal(1, handler.Disposals));
        Assert.Equal(0, view.Disposals);
    }

    [Fact]
    public async Task Replacing_from_game_removes_its_popups_and_children_without_resuming_the_old_game ()
    {
        List<Handler<BootRoute>> boots = new();
        List<Handler<GameRoute>> games = new();
        List<Handler<PopupRoute>> popups = new();
        List<Handler<HudRoute>> huds = new();
        ScreenDefinition<GameRoute> game = new((creation, _) =>
        {
            if (games.Count > 0)
            {
                Assert.Equal(1, games[^1].Disposals);
                Assert.All(huds, handler => Assert.Equal(1, handler.Disposals));
            }
            creation.RegisterScreens(Hud, screens => screens.RegisterScreen(Define(huds)));
            Handler<GameRoute> handler = creation.Lifetime.CreateOwned(() => new Handler<GameRoute>());
            games.Add(handler);
            return new(handler);
        });
        ScreenCatalog catalog = ScreenCatalog.Build(Root, RegionCompositionMode.Layered, screens =>
        {
            screens.Register(Define(boots), Configure);
            screens.Register(game, route =>
            {
                Configure(route);
                route.AddChildRegion(Hud, RegionCompositionMode.Layered, RegionOccupancy.Required,
                    child => child.AddRoute<HudRoute>(Configure));
            });
            screens.Register(Define(popups, ScreenInstancePolicy.Multiple), Configure);
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new BootRoute());
        NavigationEntryId bootVisit = boots[0].Activity!.EntryId;
        await host.Client.PushAsync(host.Root, Destination.For(new GameRoute(1)).Child(Hud, Destination.For(new HudRoute(1))));
        await host.Client.PushAsync(host.Root, Destination.For(new PopupRoute()));
        await host.Client.PushAsync(host.Root, Destination.For(new PopupRoute()));
        ScreenActivityContext oldPopup = popups[^1].Activity!;
        NavigationEntryId oldGame = games[0].Activity!.EntryId;
        await oldPopup.Navigation.GetRegionNavigation(RegionTarget.Root).ReplaceFromAsync(
            HistoryTarget.Unique<GameRoute>(),
            Destination.For(new GameRoute(2)).Child(Hud, Destination.For(new HudRoute(0))),
            new NavigationOptions { RecreateInstance = true });

        Assert.Equal(new[] { bootVisit, games[1].Activity!.EntryId }, host.State.Current.GetRegion(host.Root).Entries);
        Assert.False(host.State.Current.Entries.ContainsKey(oldGame));
        Assert.Equal(1, games[0].Activations);
        Assert.All(popups, handler => Assert.Equal(1, handler.Disposals));
        Assert.Equal(new GameRoute(2), games[1].Route);
        Assert.Equal(new HudRoute(0), huds[1].Route);
        Assert.Null(huds[1].RestoredState);
        Assert.Equal(0, boots[0].Disposals);
        await Assert.ThrowsAsync<NavigationException>(() => oldPopup.Navigation.BackAsync());
    }

    [Fact]
    public async Task Ambiguous_or_missing_targets_are_rejected_without_stopping_activity_and_entry_ids_select_the_exact_visit ()
    {
        List<Handler<GameRoute>> games = new();
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(Root, RegionCompositionMode.Layered,
            screens => screens.Register(Define(games, ScreenInstancePolicy.Multiple), Configure)));
        await host.StartAsync(new GameRoute(1));
        await host.Client.PushAsync(host.Root, Destination.For(new GameRoute(2)));
        ScreenActivityContext top = games[1].Activity!;
        NavigationEntryId first = games[0].Activity!.EntryId;
        long revision = host.State.Current.Revision;
        await Assert.ThrowsAsync<NavigationException>(() => top.Navigation.ReplaceFromAsync<GameRoute>(new GameRoute(3)));
        await Assert.ThrowsAsync<NavigationException>(() => top.Navigation.ReplaceFromAsync<BootRoute>(new GameRoute(3)));
        await Assert.ThrowsAsync<NavigationException>(() => top.Navigation.ReplaceFromAsync(
            HistoryTarget.Entry(new NavigationEntryId(Guid.NewGuid())), Destination.For(new GameRoute(3))));
        Assert.Equal(revision, host.State.Current.Revision);
        Assert.False(top.CancellationToken.IsCancellationRequested);
        Assert.Equal(2, games.Count);
        await top.Navigation.ReplaceFromAsync(HistoryTarget.Entry(first), Destination.For(new GameRoute(3)));
        Assert.Single(host.State.Current.Entries);
        Assert.Equal(new GameRoute(3), games[2].Route);
        Assert.Equal(1, games[0].Disposals);
        Assert.Equal(1, games[1].Disposals);
        await Assert.ThrowsAsync<NavigationException>(() => games[2].Activity!.Navigation.ReplaceFromAsync(
            HistoryTarget.Entry(first), Destination.For(new GameRoute(4))));
    }

    [Fact]
    public async Task A_history_target_cannot_select_an_entry_in_another_region ()
    {
        List<Handler<GameRoute>> games = new();
        List<Handler<HudRoute>> huds = new();
        ScreenCatalog catalog = ScreenCatalog.Build(Root, RegionCompositionMode.Layered, screens =>
        {
            screens.Register(Define(games), route =>
            {
                Configure(route);
                route.AddChildRegion(Hud, RegionCompositionMode.Layered, RegionOccupancy.Required,
                    child => child.AddRoute<HudRoute>(Configure));
            });
            screens.RegisterScreens(Hud, child => child.RegisterScreen(Define(huds)));
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        NavigationDestinationTree<GameRoute> destination = Destination.For(new GameRoute(1))
            .Child(Hud, Destination.For(new HudRoute(1)));
        await host.StartAsync(destination);
        ScreenActivityContext childActivity = huds[0].Activity!;
        long revision = host.State.Current.Revision;

        await Assert.ThrowsAsync<NavigationException>(() => host.Client.ReplaceFromAsync(host.Root,
            HistoryTarget.Entry(childActivity.EntryId), destination));
        await Assert.ThrowsAsync<NavigationConfigurationException>(() => host.Client.ReplaceFromAsync(
            new RegionInstanceId(Guid.NewGuid()), HistoryTarget.Current, destination));

        Assert.Equal(revision, host.State.Current.Revision);
        Assert.False(childActivity.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, games[0].Disposals);
        Assert.Equal(0, huds[0].Disposals);
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single)]
    [InlineData(ScreenInstancePolicy.Multiple)]
    public async Task Recreate_waits_for_previous_disposal_before_reporting_success (ScreenInstancePolicy policy)
    {
        TaskCompletionSource<object?> disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<object?> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Handler<GameRoute>> games = new();
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register(Define(games, policy), Configure)));
        await host.StartAsync(new GameRoute(1));
        games[0].OnDispose = async () =>
        {
            disposing.TrySetResult(null);
            await release.Task;
        };
        Task restart = games[0].Activity!.Navigation.ReplaceAsync(new GameRoute(2), new NavigationOptions { RecreateInstance = true });
        try
        {
            await disposing.Task.WaitAsync(Timeout);
            Assert.False(restart.IsCompleted);
            if (policy == ScreenInstancePolicy.Single)
            {
                Assert.Single(games);
            }
        }
        finally
        {
            release.TrySetResult(null);
        }
        await restart.WaitAsync(Timeout);
        Assert.Equal(2, games.Count);
        Assert.Equal(1, games[0].Disposals);
        Assert.Equal(new GameRoute(2), games[1].Route);
    }

    [Fact]
    public async Task Fresh_push_saves_the_previous_visit_and_back_restores_it_without_reusing_its_old_scope ()
    {
        List<Handler<GameRoute>> games = new();
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register(Define(games), Configure)));
        await host.StartAsync(new GameRoute(1));
        NavigationEntryId first = games[0].Activity!.EntryId;
        games[0].SavedState = 42;
        await games[0].Activity!.Navigation.PushAsync(new GameRoute(2), new NavigationOptions { RecreateInstance = true });
        Assert.Equal(2, host.State.Current.Entries.Count);
        Assert.Equal(1, games[0].Disposals);
        Assert.Null(games[1].RestoredState);
        await games[1].Activity!.Navigation.BackAsync();
        Assert.Equal(first, games[1].Activity!.EntryId);
        Assert.Equal(new GameRoute(1), games[1].Route);
        Assert.Equal(42, games[1].RestoredState);
        Assert.Equal(2, games.Count);
    }

    [Fact]
    public async Task Failed_release_does_not_construct_a_second_single_instance_or_repeat_disposal ()
    {
        List<Handler<GameRoute>> games = new();
        NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register(Define(games), Configure)));
        await host.StartAsync(new GameRoute(1));
        games[0].OnDispose = () => throw new InvalidOperationException("Still using a resource.");
        await Assert.ThrowsAsync<NavigationException>(() => games[0].Activity!.Navigation.ReplaceAsync(new GameRoute(2),
            new NavigationOptions { RecreateInstance = true }));
        Assert.Single(games);
        Assert.Equal(1, games[0].Disposals);
        Assert.True(games[0].Activity!.CancellationToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<Exception>(() => host.ShutdownAsync().AsTask());
        await Assert.ThrowsAnyAsync<Exception>(() => host.ShutdownAsync().AsTask());
        Assert.Equal(1, games[0].Disposals);
    }

    [Fact]
    public async Task A_single_instance_cannot_be_recreated_for_a_simultaneous_screen_transition ()
    {
        List<Handler<GameRoute>> games = new();
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register(Define(games), Configure)));
        await host.StartAsync(new GameRoute(1));
        ScreenActivityContext original = games[0].Activity!;
        NavigationTransition transition = new(NavigationTransitionScope.Region, (_, _) => throw new InvalidOperationException("Must not run."), requiresSimultaneousScreens: true);
        await Assert.ThrowsAsync<NavigationException>(() => original.Navigation.ReplaceAsync(new GameRoute(2),
            new NavigationOptions { RecreateInstance = true, Transition = transition }));
        Assert.Single(games);
        Assert.False(original.CancellationToken.IsCancellationRequested);
        Assert.Equal(0, games[0].Disposals);
    }

    [Theory]
    [InlineData(ScreenInstancePolicy.Single)]
    [InlineData(ScreenInstancePolicy.Multiple)]
    public async Task Failed_recreation_restores_the_previous_visit_without_committing_the_new_route (ScreenInstancePolicy policy)
    {
        List<Handler<GameRoute>> games = new();
        ScreenDefinition<GameRoute> definition = new((creation, _) =>
        {
            Handler<GameRoute> handler = creation.Lifetime.CreateOwned(() => new Handler<GameRoute>());
            if (games.Count == 1)
            {
                handler.OnPrepare = () => throw new InvalidOperationException("Preparation failed.");
            }
            games.Add(handler);
            return new(handler);
        }, policy);
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register(definition, Configure)));
        await host.StartAsync(new GameRoute(1));
        ScreenActivityContext original = games[0].Activity!;
        games[0].SavedState = 42;
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => original.Navigation.ReplaceAsync(new GameRoute(2),
            new NavigationOptions { RecreateInstance = true }));
        Assert.False(failure.DestinationCommitted);
        Assert.Equal(original.EntryId, Assert.Single(host.State.Current.GetRegion(host.Root).Entries));
        Assert.Equal(new GameRoute(1), host.State.Current.GetEntry(original.EntryId).Route);
        Assert.Equal(1, games[1].Disposals);
        Handler<GameRoute> restored = policy == ScreenInstancePolicy.Single ? games[2] : games[0];
        Assert.False(restored.Activity!.CancellationToken.IsCancellationRequested);
        Assert.Equal(42, policy == ScreenInstancePolicy.Single ? restored.RestoredState : restored.SavedState);
    }

    [Fact]
    public async Task An_external_owner_can_replace_a_host_without_destroying_its_borrowed_view ()
    {
        ResourceLifetime lifetime = new();
        ExternalView view = new();
        ResourceReference<ExternalView> reference = lifetime.Reference(view);
        List<Handler<BootRoute>> boots = new();
        ScreenDefinition<BootRoute> screen = new(async (creation, token) =>
        {
            await creation.Lifetime.BorrowAsync(reference, token);
            Handler<BootRoute> handler = creation.Lifetime.CreateOwned(() => new Handler<BootRoute>());
            boots.Add(handler);
            return handler;
        });
        ScreenCatalog catalog = ScreenCatalog.Build(screens => screens.Register(screen, Configure));
        NavigationHost previous = NavigationHost.Create(catalog);
        await previous.StartAsync(new BootRoute());
        ScreenActivityContext old = boots[0].Activity!;
        await previous.ShutdownAsync();
        await using NavigationHost next = NavigationHost.Create(catalog);
        await next.StartAsync(new BootRoute());
        Assert.NotEqual(previous.Root, next.Root);
        Assert.NotEqual(old.EntryId, boots[1].Activity!.EntryId);
        Assert.Equal(1, boots[0].Disposals);
        Assert.Equal(0, view.Disposals);
        await Assert.ThrowsAsync<NavigationRuntimeClosedException>(() => previous.Client.ResetAsync(previous.Root, Destination.For(new BootRoute())));
        await Assert.ThrowsAsync<NavigationException>(() => old.Navigation.ResetAsync(new BootRoute()));
        await next.ShutdownAsync();
        await lifetime.EndAsync();
        Assert.Equal(0, view.Disposals);
    }

    private static ScreenDefinition<TRoute> Define<TRoute> (List<Handler<TRoute>> handlers, ScreenInstancePolicy policy = ScreenInstancePolicy.Single) where TRoute : Route
    {
        return new ScreenDefinition<TRoute>((creation, _) =>
        {
            Handler<TRoute> handler = creation.Lifetime.CreateOwned(() => new Handler<TRoute>());
            handlers.Add(handler);
            return new(handler);
        }, policy);
    }

    private static void Configure<TRoute> (RouteDefinitionBuilder<TRoute> route) where TRoute : NavigationRoute
    {
        route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
        route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
    }

    private sealed class ExternalView : IDisposable
    {
        public int Disposals
        {
            get; private set;
        }
        public void Dispose () => Disposals++;
    }

    private sealed class Handler<TRoute> : IScreenLifecycleHandler<TRoute>, IScreenStateCapture, IAsyncDisposable where TRoute : Route
    {
        public TRoute? Route
        {
            get; private set;
        }
        public ScreenActivityContext? Activity
        {
            get; private set;
        }
        public object? SavedState
        {
            get; set;
        }
        public object? RestoredState
        {
            get; private set;
        }
        public int Activations
        {
            get; private set;
        }
        public int Disposals
        {
            get; private set;
        }
        public Action? OnPrepare
        {
            get; set;
        }
        public Func<Task>? OnDispose
        {
            get; set;
        }

        public object? CaptureState () => SavedState;
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Route = route;
            RestoredState = preparation.SavedState;
            OnPrepare?.Invoke();
            return default;
        }
        public ValueTask ActivateAsync (TRoute route, ScreenActivityContext activity)
        {
            Activity = activity;
            Activations++;
            return default;
        }
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
        public async ValueTask DisposeAsync ()
        {
            Disposals++;
            if (OnDispose is not null)
            {
                await OnDispose();
            }
        }
    }
}
