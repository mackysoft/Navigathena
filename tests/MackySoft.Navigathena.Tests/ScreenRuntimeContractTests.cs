using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenRuntimeContractTests
{
    private static readonly RegionDefinitionId Root = new("game");

    [Fact]
    public async Task Independent_children_prepare_concurrently_without_restarting_the_parent_and_keep_native_order ()
    {
        RegionDefinitionId left = new("left");
        RegionDefinitionId right = new("right");
        ConcurrentQueue<string> events = new();
        RecordingScreen parent = new("parent", events);
        RecordingScreen leftScreen = new("left", events);
        RecordingScreen rightScreen = new("right", events);
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Exclusive, root => root.AddRoute<FirstRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            route.AddChildRegion(left, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<SecondRoute>(item =>
{
    item.AllowedEntryOperations = RouteEntryOperations.Push;
    item.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
}));
            route.AddChildRegion(right, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<SecondRoute>(item =>
{
    item.AllowedEntryOperations = RouteEntryOperations.Push;
    item.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
}));
        }));
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
        {
            builder.RegisterScreens(Root, screens => Register<FirstRoute>(screens, (preparation, _) =>
            {
                preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { parent.View }));
                return new ValueTask<RecordingScreen>(parent);
            }));
            builder.RegisterScreens(left, screens => Register<SecondRoute>(screens, async (preparation, _) =>
            {
                entered.SetResult(true);
                await proceed.Task;
                preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { leftScreen.View }));
                return leftScreen;
            }));
            builder.RegisterScreens(right, screens => Register<SecondRoute>(screens, (preparation, _) =>
            {
                preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { rightScreen.View }));
                return new ValueTask<RecordingScreen>(rightScreen);
            }));
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new FirstRoute());
        IScreenNavigation navigation = parent.Activities.Single().Navigation;
        NavigationOperation leftOperation = navigation.GetRegionNavigation(RegionTarget.Child(left)).Push(new SecondRoute());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        NavigationResult rightResult = await navigation.GetRegionNavigation(RegionTarget.Child(right)).Push(new SecondRoute()).WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        proceed.SetResult(true);
        Assert.Equal(NavigationResultKind.Committed, rightResult.Kind);
        Assert.Equal(NavigationResultKind.Committed, (await leftOperation.WaitAsync()).Kind);
        Assert.Single(parent.Activities);
        Assert.Equal(new[] { right, left }, parent.Changes.SelectMany(change => change.ChildChanges).Select(change => change.DefinitionId));
        Assert.False(parent.Activities[0].CancellationToken.IsCancellationRequested);
        Assert.True(parent.View.Presentation.InputEnabled);
        Assert.True(rightScreen.View.Presentation.InputEnabled);
        Assert.True(parent.View.Presentation.Order < leftScreen.View.Presentation.Order);
        Assert.True(leftScreen.View.Presentation.Order < rightScreen.View.Presentation.Order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detached_screen_cleanup_is_observable_without_delaying_navigation_and_shutdown_joins_it (bool fail)
    {
        ConcurrentQueue<string> events = new();
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingScreen first = new("first", events)
        {
            DisposeBarrier = release.Task,
            FailDispose = fail
        };
        RecordingScreen second = new("second", events);
        ScreenCatalog catalog = CreateCatalog(async (preparation, token) =>
        {
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("first.view", events), token);
            return first;
        }, (_, _) => new ValueTask<RecordingScreen>(second));
        NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new FirstRoute());

        NavigationResult result = await first.Activities[0].Navigation.Replace(new SecondRoute()).WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NavigationResultKind.Committed, result.Kind);
        Assert.Equal(NavigationPresentationStatus.Ready, result.PresentationStatus);
        NavigationTerminationSnapshot pending = host.Terminations.Current;
        NavigationTerminationRecord record = Assert.Single(pending.Records);
        Assert.Equal(NavigationTerminationStatus.Pending, record.Status);
        Assert.Equal(NavigationTerminationKind.Screen, record.Kind);
        Assert.Single(second.Activities);
        Task<NavigationTerminationSnapshot> change = host.Terminations.WaitForChangeAsync(pending.Revision).AsTask();
        Task shutdown = host.ShutdownAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        release.SetResult(true);
        if (fail)
        {
            await Assert.ThrowsAsync<AggregateException>(() => shutdown.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        }
        await change.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, events.Count(item => item == "first.dispose"));
        if (fail)
        {
            Assert.Equal(NavigationTerminationStatus.Failed, Assert.Single(host.Terminations.Current.Records).Status);
            Assert.DoesNotContain("first.view.release", events);
        }
        else
        {
            Assert.Empty(host.Terminations.Current.Records);
            Assert.Contains("first.view.release", events);
        }
    }

    [Fact]
    public async Task Cancelling_a_wait_does_not_cancel_an_accepted_operation_and_conflicts_are_returned_immediately ()
    {
        ConcurrentQueue<string> events = new();
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingScreen first = new("first", events);
        ScreenCatalog catalog = CreateCatalog((_, _) => new ValueTask<RecordingScreen>(first), async (_, token) =>
        {
            entered.SetResult(true);
            await proceed.Task;
            Assert.False(token.IsCancellationRequested);
            return new RecordingScreen("second", events);
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new FirstRoute());
        NavigationOperation operation = first.Activities[0].Navigation.Push(new SecondRoute());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using CancellationTokenSource wait = new();
        Task<NavigationResult> waiter = operation.WaitAsync(wait.Token);
        wait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        NavigationOperation conflict = host.Client.Push(host.Root, Destination.For(new SecondRoute()));
        Assert.Equal(NavigationResultKind.Conflict, (await conflict.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5))).Kind);
        proceed.SetResult(true);
        Assert.Equal(NavigationResultKind.Committed, (await operation.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5))).Kind);
    }

    [Fact]
    public async Task Failed_activity_stop_closes_input_and_requires_recovery_instead_of_reopening_the_source ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events)
        {
            FailDeactivate = true
        };
        ScreenCatalog catalog = CreateCatalog((preparation, _) =>
        {
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { first.View }));
            return new ValueTask<RecordingScreen>(first);
        });
        NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new FirstRoute());

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await first.Activities[0].Navigation.Push(new SecondRoute()).WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(result.DestinationCommitted);
        Assert.Equal(NavigationPresentationStatus.RecoveryRequired, result.PresentationStatus);
        Assert.NotNull(host.State.Current.HostIncident);
        Assert.False(first.View.Presentation.InputEnabled);
        Assert.Single(first.Activities);
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());
    }

    [Fact]
    public async Task Startup_borrows_the_existing_overlay_keeps_it_present_during_loading_and_reveals_before_activity ()
    {
        ConcurrentQueue<string> events = new();
        RecordingView overlay = new();
        overlay.Apply(new ViewPresentation(true, false, 100));
        ResourceLifetime overlayLifetime = new();
        ResourceReference<RecordingView> reference = overlayLifetime.Reference(overlay);
        RecordingScreen title = new("title", events);
        RecordingEffect effect = new(events);
        ScreenCatalog catalog = CreateCatalog((preparation, _) =>
        {
            Assert.True(overlay.Presentation.OutputEnabled);
            Assert.Contains("effect.begin", events);
            events.Enqueue("title.load");
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { title.View }));
            return new ValueTask<RecordingScreen>(title);
        });
        NavigationTransition reveal = new(NavigationTransitionScope.Host, async (preparation, token) =>
        {
            RecordingView existing = await preparation.Lifetime.BorrowAsync(reference, token);
            preparation.RegisterExistingViewAdapter(existing);
            Assert.True(existing.Presentation.OutputEnabled);
            return preparation.Lifetime.CreateOwned(() => effect);
        });
        await using NavigationHost host = NavigationHost.Create(catalog);

        NavigationResult result = await host.Start(new FirstRoute(), new NavigationOptions { Transition = reveal }).WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, result.Kind);
        Assert.Equal(new[] { "effect.begin", "title.load", "title.initialize", "effect.prepare", "effect.after", "effect.settle.Destination", "effect.dispose", "title.activate" }, events);
        Assert.True(effect.Revealed);
        Assert.True(overlay.IsAlive);
        Assert.True(overlay.Presentation.OutputEnabled);
        await overlayLifetime.EndAsync();
        Assert.DoesNotContain("title.dispose", events);
    }

    [Fact]
    public async Task Destination_screen_effect_is_resolved_after_initialization_and_reused_until_screen_termination ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events);
        RecordingScreen second = new("second", events);
        RecordingEffect effect = new(events);
        ScreenCatalog catalog = CreateCatalog((preparation, _) =>
        {
            events.Enqueue("first.load");
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { first.View }));
            preparation.SetTransitionEffect(preparation.Lifetime.CreateOwned(() => effect), new RecordingView());
            return new ValueTask<RecordingScreen>(first);
        }, (preparation, _) =>
        {
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { second.View }));
            return new ValueTask<RecordingScreen>(second);
        });
        RegionNavigationOptions region = new();
        region.Transitions.On(NavigationOperationKind.Reset).To<FirstRoute>().Use(NavigationTransition.FromDestinationScreen(NavigationTransitionScope.Region));
        region.Transitions.On(NavigationOperationKind.Back).To<FirstRoute>().Use(NavigationTransition.FromDestinationScreen(NavigationTransitionScope.Region));
        await using NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions { Regions = new Dictionary<RegionDefinitionId, RegionNavigationOptions> { [Root] = region } });

        Assert.Equal(NavigationResultKind.Committed, (await host.Start(new FirstRoute()).WaitAsync()).Kind);
        await first.Activities[0].Navigation.PushAsync(new SecondRoute());
        Assert.Equal(NavigationResultKind.Committed, (await second.Activities[0].Navigation.Back().WaitAsync()).Kind);

        Assert.True(Array.IndexOf(events.ToArray(), "first.initialize") < Array.IndexOf(events.ToArray(), "effect.begin"));
        Assert.Equal(1, events.Count(item => item == "first.load"));
        Assert.Equal(2, events.Count(item => item == "effect.begin"));
        Assert.DoesNotContain("effect.dispose", events);
        await host.ShutdownAsync();
        Assert.Equal(1, events.Count(item => item == "effect.dispose"));
    }

    [Fact]
    public async Task Ambiguous_transition_rules_fail_before_activity_changes_and_explicit_none_overrides_policy ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events);
        ScreenCatalog catalog = CreateCatalog((_, _) => new ValueTask<RecordingScreen>(first), (_, _) => new ValueTask<RecordingScreen>(new RecordingScreen("second", events)));
        RegionNavigationOptions region = new();
        region.Transitions.On(NavigationOperationKind.Push).From<FirstRoute>().Use(new NavigationTransition(NavigationTransitionScope.Region, (preparation, _) => new ValueTask<INavigationTransitionEffect>(preparation.Lifetime.CreateOwned(() => new RecordingEffect(events)))));
        region.Transitions.On(NavigationOperationKind.Push).To<SecondRoute>().Use(new NavigationTransition(NavigationTransitionScope.Region, (preparation, _) => new ValueTask<INavigationTransitionEffect>(preparation.Lifetime.CreateOwned(() => new RecordingEffect(events)))));
        await using NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions { Regions = new Dictionary<RegionDefinitionId, RegionNavigationOptions> { [Root] = region } });
        await host.StartAsync(new FirstRoute());

        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await first.Activities[0].Navigation.PushAsync(new SecondRoute()));

        Assert.DoesNotContain("first.deactivate", events);
        Assert.DoesNotContain("effect.begin", events);
        NavigationResult explicitNone = await host.Client.Push(host.Root, Destination.For(new SecondRoute()), new NavigationOptions { Transition = NavigationTransition.None }).WaitAsync();
        Assert.Equal(NavigationResultKind.Committed, explicitNone.Kind);
    }

    [Fact]
    public async Task Effect_disposal_failure_does_not_undo_commit_or_release_its_borrowed_resources ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen title = new("title", events);
        RecordingEffect effect = new(events)
        {
            FailDispose = true
        };
        ScreenCatalog catalog = CreateCatalog((_, _) => new ValueTask<RecordingScreen>(title));
        NavigationTransition transition = new(NavigationTransitionScope.Host, async (preparation, token) =>
        {
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("effect", events), token);
            return preparation.Lifetime.CreateOwned(() => effect);
        });
        NavigationHost host = NavigationHost.Create(catalog);

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.StartAsync(new FirstRoute(), new NavigationOptions { Transition = transition }));
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());

        Assert.True(result.DestinationCommitted);

        Assert.Equal(1, events.Count(item => item == "effect.dispose"));
        Assert.DoesNotContain("effect.release", events);
        Assert.DoesNotContain("title.activate", events);
    }

    [Fact]
    public async Task A_retained_screen_stops_logic_without_disposing_its_view_and_returns_with_a_new_activity ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events);
        RecordingScreen second = new("second", events);
        ScreenCreationContext<FirstRoute>? captured = null;
        ScreenCatalog catalog = CreateCatalog(async (preparation, token) =>
        {
            captured = preparation;
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("first", events), token);
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { first.View }));
            return first;
        }, (preparation, _) =>
        {
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { second.View }));
            return new ValueTask<RecordingScreen>(second);
        });
        await using NavigationHost host = NavigationHost.Create(catalog);

        NavigationResult start = await host.Start(Destination.For(new FirstRoute())).WaitAsync();
        Assert.Equal(NavigationResultKind.Committed, start.Kind);
        Assert.True(first.View.Presentation.InputEnabled);
        ScreenActivityContext oldActivity = first.Activities.Single();
        Assert.Throws<InvalidOperationException>(() => captured!.ConnectPresentation(new ScreenPresentationBinding(new[] { new RecordingView() })));

        NavigationResult push = await oldActivity.Navigation.Push(new SecondRoute()).WaitAsync();
        Assert.Equal(NavigationResultKind.Committed, push.Kind);
        Assert.True(oldActivity.CancellationToken.IsCancellationRequested);
        Assert.False(first.View.Presentation.OutputEnabled);
        Assert.DoesNotContain("first.dispose", events);
        Assert.DoesNotContain("first.release", events);
        Assert.True(Array.IndexOf(events.ToArray(), "first.deactivate") < Array.IndexOf(events.ToArray(), "second.initialize"));

        NavigationResult back = await second.Activities.Single().Navigation.Back().WaitAsync();
        Assert.Equal(NavigationResultKind.Committed, back.Kind);
        Assert.Equal(2, first.Activities.Count);
        Assert.False(first.Activities[1].CancellationToken.IsCancellationRequested);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.Equal(1, events.Count(item => item == "first.initialize"));
        Assert.Equal(NavigationResultKind.Rejected, (await oldActivity.Navigation.Push(new SecondRoute()).WaitAsync()).Kind);
    }

    [Fact]
    public async Task Failed_factory_releases_partial_acquisition_before_restoring_source_activity ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events);
        ScreenCatalog catalog = CreateCatalog((preparation, _) =>
        {
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { first.View }));
            return new ValueTask<RecordingScreen>(first);
        }, async (preparation, token) =>
        {
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("partial", events, true), token);
            throw new InvalidOperationException("unreachable");
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new FirstRoute()));

        NavigationException failed = await Assert.ThrowsAsync<NavigationException>(async () => await first.Activities[0].Navigation.PushAsync(new SecondRoute()));

        Assert.False(failed.DestinationCommitted);
        Assert.Contains("partial.acquire", events);
        Assert.Contains("partial.release", events);
        Assert.Equal(2, first.Activities.Count);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.IsType<FirstRoute>(host.State.Current.GetEntry(host.State.Current.GetRegion(host.Root).Entries.Single()).Route);
    }

    [Fact]
    public async Task Shutdown_joins_and_keeps_view_ownership_when_lifecycle_handler_disposal_fails ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events)
        {
            FailDispose = true
        };
        ScreenCatalog catalog = CreateCatalog(async (preparation, token) =>
        {
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("view", events), token);
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { first.View }));
            return first;
        });
        NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new FirstRoute()));

        AggregateException result = await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());
        AggregateException joined = await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());

        Assert.Single(result.Flatten().InnerExceptions);
        Assert.Same(result, joined);
        Assert.Equal(1, events.Count(item => item == "first.dispose"));
        Assert.DoesNotContain("view.release", events);
        Assert.True(first.Activities.Single().CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task External_owner_can_end_usage_before_host_and_keeps_ownership_of_its_value ()
    {
        ConcurrentQueue<string> events = new();
        ResourceLifetime lifetime = new();
        RecordingView original = new();
        ResourceReference<RecordingView> reference = lifetime.Reference(original);
        RecordingScreen first = new("first", events);
        ScreenCatalog catalog = CreateCatalog(async (preparation, token) =>
        {
            RecordingView view = await preparation.Lifetime.BorrowAsync(reference, token);
            Assert.Same(original, view);
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { view }));
            return first;
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new FirstRoute()));

        await lifetime.EndAsync();

        Assert.Contains("first.deactivate", events);
        Assert.Contains("first.dispose", events);
        Assert.True(original.IsAlive);
        Assert.False(original.Presentation.OutputEnabled);
        Assert.Throws<InvalidOperationException>(() => lifetime.Reference(original));
        await host.ShutdownAsync();
    }

    [Fact]
    public async Task Different_adapters_cannot_acquire_the_same_physical_view ()
    {
        ConcurrentQueue<string> events = new();
        object identity = new();
        RecordingScreen first = new("first", events);
        ScreenCatalog catalog = CreateCatalog((preparation, _) =>
        {
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { new RecordingView(identity) }));
            return new ValueTask<RecordingScreen>(first);
        }, (preparation, _) =>
        {
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { new RecordingView(identity) }));
            return new ValueTask<RecordingScreen>(new RecordingScreen("second", events));
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new FirstRoute()));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await first.Activities[0].Navigation.PushAsync(new SecondRoute()));

        Assert.False(result.DestinationCommitted);
        Assert.Equal(2, first.Activities.Count);
    }

    [Fact]
    public async Task Blockers_reuse_common_content_switch_custom_content_immediately_and_do_not_become_history ()
    {
        ConcurrentQueue<string> events = new();
        RecordingBlocker common = new();
        RecordingBlocker custom = new();
        int commonCreated = 0;
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<FirstRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Reset;
    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
});
            root.AddRoute<SecondRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push;
    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
});
            root.AddRoute<ModalRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push;
    route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
});
            root.AddRoute<CustomModalRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push;
    route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
});
        });
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, screens =>
        {
            Register<FirstRoute>(screens, (preparation, _) => Make("first", preparation));
            Register<SecondRoute>(screens, (preparation, _) => Make("full", preparation));
            Register<ModalRoute>(screens, (preparation, _) => Make("modal", preparation));
            Register<CustomModalRoute>(screens, (preparation, _) => Make("custom", preparation));
            screens.RegisterBlocker<CustomModalRoute>(new BlockerDefinition((preparation, _) => new ValueTask<IBlockerPresenter>(preparation.Lifetime.CreateOwned(() => custom))));
        }));
        ValueTask<RecordingScreen> Make<T> (string name, ScreenCreationContext<T> preparation) where T : Route
        {
            RecordingScreen screen = new(name, events);
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }));
            return new ValueTask<RecordingScreen>(screen);
        }
        await using NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions
        {
            DefaultBlocker = new BlockerDefinition((preparation, _) =>
            {
                commonCreated++;
                return new ValueTask<IBlockerPresenter>(preparation.Lifetime.CreateOwned(() => common));
            })
        });
        await host.StartAsync(new FirstRoute());
        Assert.Equal(0, commonCreated);
        Assert.Equal(NavigationResultKind.Committed, (await host.Client.Push(host.Root, Destination.For(new ModalRoute())).WaitAsync()).Kind);
        BlockerScreenContext oldConnection = common.Context!;
        await host.Client.PushAsync(host.Root, Destination.For(new ModalRoute()));
        Assert.Equal(1, commonCreated);
        Assert.Equal(1, common.EnterCount);
        Assert.NotEqual(oldConnection.EntryId, common.Context!.EntryId);
        Assert.Equal(NavigationResultKind.Rejected, (await oldConnection.Navigation.Back().WaitAsync()).Kind);
        await host.Client.PushAsync(host.Root, Destination.For(new CustomModalRoute()));
        Assert.False(common.View.Presentation.OutputEnabled);
        Assert.True(custom.View.Presentation.OutputEnabled);
        Assert.Equal(0, custom.EnterCount);
        Assert.Equal(0, common.ExitCount);
        Assert.Equal(4, host.State.Current.GetRegion(host.Root).Entries.Count);
        await host.Client.PushAsync(host.Root, Destination.For(new SecondRoute()));
        Assert.False(custom.View.Presentation.OutputEnabled);
        Assert.False(custom.View.Presentation.InputEnabled);
        Assert.Null(custom.Context);
        Assert.Equal(0, custom.DisposeCount);
        await host.Client.BackAsync(host.Root);
        Assert.True(custom.View.Presentation.OutputEnabled);
        await host.Client.BackAsync(host.Root);
        Assert.True(common.View.Presentation.OutputEnabled);
        Assert.Equal(0, custom.DisposeCount);
        Assert.Equal(0, common.DisposeCount);
        await host.Client.BackAsync(host.Root);
        await host.Client.BackAsync(host.Root);
        Assert.Equal(1, common.ExitCount);
        Assert.Null(common.Context);
        Assert.False(common.View.Presentation.InputEnabled);
        await host.ShutdownAsync();
        Assert.Equal(1, common.DisposeCount);
        Assert.Equal(1, custom.DisposeCount);
    }

    [Fact]
    public async Task Outgoing_first_releases_before_loading_and_restores_the_source_on_destination_failure ()
    {
        ConcurrentQueue<string> events = new();
        int created = 0;
        ScreenCatalog catalog = CreateCatalog(async (preparation, token) =>
        {
            created++;

            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("first.resource." + created, events), token);
            RecordingScreen first = new("first." + created, events)
            {
                SavedState = "selected-item",
                ExpectedRestoration = created == 2 ? "selected-item" : null
            };
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { first.View }));
            return first;
        }, (_, _) =>
        {
            Assert.Contains("first.resource.1.release", events);
            throw new InvalidOperationException("Destination load failed.");
        });
        await using NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions
        {
            Regions = new Dictionary<RegionDefinitionId, RegionNavigationOptions> { [Root] = new() { ResourcePolicy = ScreenResourcePolicy.OutgoingFirst } }
        });
        await host.StartAsync(new FirstRoute());

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ReplaceAsync(host.Root, Destination.For(new SecondRoute())));

        Assert.False(result.DestinationCommitted);

        Assert.Equal(RestorationOutcome.Restored, result.Restoration);
        Assert.Equal(NavigationPresentationStatus.Ready, result.PresentationStatus);
        Assert.Equal(2, created);
        Assert.Contains("first.2.activate", events);
    }

    [Fact]
    public async Task Outgoing_first_rejects_a_simultaneous_screen_effect_before_stopping_activity ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen first = new("first", events);
        await using NavigationHost host = NavigationHost.Create(CreateCatalog((_, _) => new ValueTask<RecordingScreen>(first)), new NavigationHostOptions
        {
            Regions = new Dictionary<RegionDefinitionId, RegionNavigationOptions> { [Root] = new() { ResourcePolicy = ScreenResourcePolicy.OutgoingFirst } }
        });
        await host.StartAsync(new FirstRoute());
        NavigationTransition transition = new(NavigationTransitionScope.Region, (_, _) => throw new InvalidOperationException("Must not create the effect."), requiresSimultaneousScreens: true);
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await first.Activities[0].Navigation.ReplaceAsync(new SecondRoute(), new NavigationOptions { Transition = transition }));
        Assert.False(result.DestinationCommitted);
        Assert.DoesNotContain("first.deactivate", events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_recovery_releases_independent_fresh_resources_while_old_termination_is_failed_or_pending (bool pending)
    {
        ConcurrentQueue<string> events = new();
        TaskCompletionSource<object?> releaseOld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingScreen old = new("old", events)
        {
            FailDispose = !pending,
            DisposeBarrier = pending ? releaseOld.Task : null
        };
        int generation = 0;
        NavigationHost host = NavigationHost.Create(CreateCatalog(async (preparation, token) =>
        {
            generation++;
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("resource." + generation, events), token);
            if (generation > 1)
            {
                throw new InvalidOperationException("Fresh screen preparation failed.");
            }

            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { old.View }));
            return old;
        }));
        await host.StartAsync(new FirstRoute());
        NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries.Single();
        old.View.Lose();
        while (host.State.Current.GetPresentation(entry).IncidentId is null)
        {
            await host.State.WaitForChangeAsync(host.State.Current.Revision);
        }

        NavigationIncidentId incident = host.State.Current.GetPresentation(entry).IncidentId!.Value;

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Recovery.RecoverAsync(incident));

        Assert.False(result.DestinationCommitted);
        Assert.Contains("resource.2.release", events);
        Assert.DoesNotContain("resource.1.release", events);
        releaseOld.TrySetResult(null);
        if (pending)
        {
            await host.ShutdownAsync();
        }
        else
        {
            await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());
        }
    }

    [Fact]
    public async Task Failed_recovery_keeps_resources_still_used_by_its_own_failed_lifecycle_handler ()
    {
        ConcurrentQueue<string> events = new();
        RecordingScreen old = new("old", events)
        {
            FailDispose = true
        };
        int generation = 0;
        NavigationHost host = NavigationHost.Create(CreateCatalog(async (preparation, token) =>
        {
            generation++;
            await preparation.Lifetime.AcquireAsync(new RecordingAcquisition("resource." + generation, events), token);
            RecordingScreen screen = generation == 1 ? old : new RecordingScreen("fresh", events) { FailInitialize = true, FailDispose = true };
            preparation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }));
            return screen;
        }));
        await host.StartAsync(new FirstRoute());
        NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries.Single();
        old.View.Lose();
        while (host.State.Current.GetPresentation(entry).IncidentId is null)
        {
            await host.State.WaitForChangeAsync(host.State.Current.Revision);
        }

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Recovery.RecoverAsync(host.State.Current.GetPresentation(entry).IncidentId!.Value));
        Assert.False(result.DestinationCommitted);
        Assert.DoesNotContain("resource.2.release", events);
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());
    }

    private sealed record ModalRoute : Route;
    private sealed record CustomModalRoute : Route;
    private sealed class RecordingBlocker : IBlockerPresenter, IBlockerAnimator, IAsyncDisposable
    {
        public RecordingView View { get; } = new();
        public BlockerScreenContext? Context
        {
            get; private set;
        }
        public ValueTask TerminateAsync () => default;
        public int EnterCount
        {
            get; private set;
        }
        public int ExitCount
        {
            get; private set;
        }
        public int DisposeCount
        {
            get; private set;
        }
        public ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken)
        {
            preparation.RegisterViewAdapter(View);
            return default;
        }
        public void SetScreenContext (BlockerScreenContext? context) => Context = context;
        public ValueTask PlayEnterAsync (CancellationToken cancellationToken)
        {
            EnterCount++;
            return default;
        }
        public ValueTask PlayExitAsync (CancellationToken cancellationToken)
        {
            ExitCount++;
            return default;
        }
        public void SetAppearanceImmediately (bool shown)
        {
        }
        public ValueTask DisposeAsync ()
        {
            DisposeCount++;
            return default;
        }
    }

    private static void Register<T> (RegionScreenCatalogBuilder screens, Func<ScreenCreationContext<T>, CancellationToken, ValueTask<RecordingScreen>> create) where T : Route
    {
        screens.RegisterScreen(new ScreenDefinition<T>(async (creation, token) =>
        {
            return await create(creation, token);
        }, ScreenInstancePolicy.Multiple));
    }

    private static ScreenCatalog CreateCatalog (Func<ScreenCreationContext<FirstRoute>, CancellationToken, ValueTask<RecordingScreen>> first, Func<ScreenCreationContext<SecondRoute>, CancellationToken, ValueTask<RecordingScreen>>? second = null)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<FirstRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            root.AddRoute<SecondRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            });
        });
        return ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(Root, screens =>
        {
            Register(screens, first);
            Register(screens, second ?? ((_, _) => throw new InvalidOperationException("Unexpected second screen.")));
        }));
    }

    private sealed record FirstRoute : Route;
    private sealed record SecondRoute : Route;

    private sealed class RecordingView : IViewAdapter
    {
        private static readonly object Domain = new();
        public RecordingView (object? identity = null) => Identity = identity ?? new object();
        public object Identity
        {
            get;
        }
        public object OrderingDomain => Domain;
        public bool IsAlive { get; private set; } = true;
        public event Action<string>? Lost;
        public void Lose ()
        {
            IsAlive = false;
            Lost?.Invoke("Native view lost.");
        }
        public ViewPresentation Presentation { get; private set; } = new(false, false, 0);
        public void Apply (ViewPresentation presentation) => Presentation = presentation;
        public void Validate (ViewPresentation presentation)
        {
        }
    }

    private sealed class RecordingScreen : IScreenLifecycleHandler<Route>, IScreenStateCapture, INavigationChangeHandler
    {
        private readonly string name;
        private readonly ConcurrentQueue<string> events;
        public RecordingScreen (string name, ConcurrentQueue<string> events)
        {
            this.name = name;
            this.events = events;
        }

        public RecordingView View { get; } = new();
        public object? SavedState
        {
            get; init;
        }
        public object? ExpectedRestoration
        {
            get; init;
        }
        public object? CaptureState () => SavedState;
        public List<PresentationChangeContext> Changes { get; } = new();
        public void OnNavigationChanged (PresentationChangeContext context) => Changes.Add(context);
        public List<ScreenActivityContext> Activities { get; } = new();
        public bool FailDispose
        {
            get; init;
        }
        public bool FailInitialize
        {
            get; init;
        }
        public bool FailDeactivate
        {
            get; init;
        }
        public Task? DisposeBarrier
        {
            get; init;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken)
        {
            events.Enqueue(name + ".initialize");
            if (FailInitialize)
            {
                throw new InvalidOperationException("Initialization failed.");
            }

            return default;
        }

        public ValueTask PrepareAsync (Route route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            if (ExpectedRestoration is not null)
            {
                Assert.Equal(ExpectedRestoration, preparation.SavedState);
            }
            return default;
        }

        public ValueTask ActivateAsync (Route route, ScreenActivityContext activity)
        {
            Assert.False(View.Presentation.InputEnabled);
            Activities.Add(activity);
            events.Enqueue(name + ".activate");
            return default;
        }

        public ValueTask DeactivateAsync ()
        {
            Assert.True(Activities.Last().CancellationToken.IsCancellationRequested);
            events.Enqueue(name + ".deactivate");
            if (FailDeactivate)
            {
                throw new InvalidOperationException("Activity could not be stopped.");
            }

            return default;
        }

        public async ValueTask TerminateAsync ()
        {
            events.Enqueue(name + ".dispose");
            if (DisposeBarrier is not null)
            {
                await DisposeBarrier;
            }

            if (FailDispose)
            {
                throw new InvalidOperationException("Lifecycle termination has not released its use of the view.");
            }

        }
    }

    private sealed class RecordingAcquisition : IResourceAcquisition<object>
    {
        private readonly string name;
        private readonly ConcurrentQueue<string> events;
        private readonly bool fail;
        public RecordingAcquisition (string name, ConcurrentQueue<string> events, bool fail = false)
        {
            this.name = name;
            this.events = events;
            this.fail = fail;
        }

        public ValueTask<object> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            events.Enqueue(name + ".acquire");
            if (fail)
            {
                throw new InvalidOperationException("Acquisition failed after creating a native resource.");
            }

            return new ValueTask<object>(new object());
        }

        public ValueTask DisposeAsync ()
        {
            events.Enqueue(name + ".release");
            return default;
        }
    }

    private sealed class RecordingEffect : INavigationTransitionEffect, IAsyncDisposable
    {
        private readonly ConcurrentQueue<string> events;
        public RecordingEffect (ConcurrentQueue<string> events) => this.events = events;
        public bool Revealed
        {
            get; private set;
        }
        public bool FailDispose
        {
            get; init;
        }
        public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken)
        {
            events.Enqueue("effect.begin");
            return default;
        }

        public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken)
        {
            events.Enqueue("effect.prepare");
            return default;
        }

        public ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken)
        {
            events.Enqueue("effect.after");
            Revealed = true;
            return default;
        }

        public ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken)
        {
            events.Enqueue("effect.settle." + context.Target);
            Revealed = context.Target == TransitionSettlementTarget.Destination;
            return default;
        }

        public ValueTask DisposeAsync ()
        {
            events.Enqueue("effect.dispose");
            if (FailDispose)
            {
                throw new InvalidOperationException("Effect still uses its acquired view.");
            }

            return default;
        }
    }
}
