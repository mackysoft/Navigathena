using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ViewInputPublicationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Default_foreground_token_does_not_authorize_operations ()
    {
        ScreenForegroundToken foreground = default;

        Assert.False(foreground.IsValid);
        Assert.Equal(CancellationToken.None, foreground.CancellationToken);
    }

    [Fact]
    public async Task Activation_can_accept_operations_with_its_token_before_its_asynchronous_callback_finishes ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        Blocker blocker = new(backend);
        await using NavigationHost host = CreateHost(screens, backend, blocker);
        await host.StartAsync(new Page(0));
        Screen home = screens[0];
        TaskCompletionSource<bool> entered = Signal();
        TaskCompletionSource<bool> finish = Signal();
        backend.AfterApply = changes =>
        {
            if (screens.Count > 1 && changes.Changes.Any(change => ReferenceEquals(change.View, screens[1].View) && change.Presentation.InputEnabled))
            {
                Assert.True(blocker.View.Presentation.InputEnabled);
            }
            return default;
        };
        backend.Activating = screen =>
        {
            if (screen.RouteId != 1)
            {
                return default;
            }
            entered.TrySetResult(true);
            return new ValueTask(finish.Task);
        };

        Task push = host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        try
        {
            await entered.Task.WaitAsync(Timeout);
            Screen popup = screens[1];
            Assert.True(Assert.Single(popup.Activities).ForegroundToken.IsValid);
            Assert.True(popup.View.Presentation.InputEnabled);
            Assert.True(blocker.View.Presentation.InputEnabled);
            Assert.False(home.View.Presentation.InputEnabled);
            backend.RequestOperation();
            Assert.Equal(1, popup.Operations);
            Assert.Equal(0, home.Operations);
            Assert.False(push.IsCompleted);
        }
        finally
        {
            finish.TrySetResult(true);
        }
        await push.WaitAsync(Timeout);
        await host.Client.BackAsync(host.Root);
        Assert.False(screens[1].Activities[0].ForegroundToken.IsValid);
        Assert.True(home.Activities[1].ForegroundToken.IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_popup_restores_its_input_boundary_before_asynchronous_return_activation_finishes (bool failedPreparation)
    {
        Backend backend = new();
        List<Screen> screens = new();
        Blocker blocker = new(backend);
        await using NavigationHost host = CreateHost(screens, backend, blocker);
        await host.StartAsync(new Page(5));
        await host.Client.PushAsync(host.Root, Destination.For(new Page(0)));
        Screen popup = screens[1];
        ScreenForegroundToken previous = Assert.Single(popup.Foregrounds);
        if (!failedPreparation)
        {
            await host.Client.PushAsync(host.Root, Destination.For(new Page(2)));
        }
        else
        {
            backend.Failure = "preparation";
        }
        TaskCompletionSource<bool> entered = Signal();
        TaskCompletionSource<bool> finish = Signal();
        backend.Activating = screen =>
        {
            if (!ReferenceEquals(screen, popup))
            {
                return default;
            }
            entered.TrySetResult(true);
            return new ValueTask(finish.Task);
        };
        backend.AfterApply = changes =>
        {
            if (changes.Changes.Any(change => ReferenceEquals(change.View, popup.View) && change.Presentation.InputEnabled))
            {
                Assert.True(blocker.View.Presentation.InputEnabled);
            }
            return default;
        };
        Task returning = failedPreparation
            ? host.Client.PushAsync(host.Root, Destination.For(new Page(1)))
            : host.Client.BackAsync(host.Root);
        try
        {
            await entered.Task.WaitAsync(Timeout);
            Assert.True(popup.Activities.Last().ForegroundToken.IsValid);
            Assert.False(previous.IsValid);
            Assert.True(popup.View.Presentation.InputEnabled);
            Assert.True(blocker.View.Presentation.OutputEnabled);
            Assert.True(blocker.View.Presentation.InputEnabled);
            Assert.InRange(blocker.View.Presentation.Order, screens[0].View.Presentation.Order + 1, popup.View.Presentation.Order - 1);
            Assert.False(returning.IsCompleted);
        }
        finally
        {
            finish.TrySetResult(true);
        }
        if (failedPreparation)
        {
            await Assert.ThrowsAsync<NavigationException>(() => returning.WaitAsync(Timeout));
        }
        else
        {
            await returning.WaitAsync(Timeout);
        }
        backend.AfterApply = null;
        Assert.True(popup.Activities.Last().ForegroundToken.IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Layered_push_closes_background_input_and_reopens_it_on_return (bool requiresBoundary)
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push,
                requiresBoundary ? LowerPresentationPolicy.Modal : LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        Screen first = Assert.Single(screens);
        Assert.True(first.View.Presentation.InputEnabled);
        ScreenActivityContext activity = Assert.Single(first.Activities);
        ScreenForegroundToken initialForeground = Assert.Single(first.Foregrounds);
        ScreenForegroundToken copiedForeground = initialForeground;
        Assert.True(initialForeground.IsValid);
        Assert.True(copiedForeground.IsValid);

        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        Screen second = screens[1];
        Assert.True(first.View.Presentation.OutputEnabled);
        Assert.False(first.View.Presentation.InputEnabled);
        Assert.True(second.View.Presentation.InputEnabled);
        Assert.False(initialForeground.IsValid);
        Assert.False(copiedForeground.IsValid);
        Assert.True(initialForeground.CancellationToken.IsCancellationRequested);
        Assert.True(activity.CancellationToken.IsCancellationRequested);
        Assert.True(first.ClosedDuringDeactivation);

        await host.Client.BackAsync(host.Root);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.False(second.View.Presentation.InputEnabled);
        Assert.Equal(2, first.Activities.Count);
        Assert.Equal(2, first.Foregrounds.Count);
        Assert.True(first.Foregrounds[1].IsValid);
        Assert.False(initialForeground.IsValid);
        Assert.False(copiedForeground.IsValid);
        Assert.All(second.Foregrounds, foreground => Assert.False(foreground.IsValid));
        await host.ShutdownAsync();
        Assert.All(screens, screen => Assert.False(screen.View.Presentation.InputEnabled));
        Assert.All(screens.SelectMany(screen => screen.Foregrounds), foreground => Assert.False(foreground.IsValid));
    }

    [Fact]
    public async Task Navigation_in_one_child_region_preserves_the_other_regions_input_permission ()
    {
        RegionDefinitionId left = new("left");
        RegionDefinitionId right = new("right");
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, region =>
            region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve, route =>
            {
                route.AddChildRegion(left, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
                    child.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
                route.AddChildRegion(right, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
                    child.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
            }));
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
        {
            foreach (RegionDefinitionId region in new[] { Root, left, right })
            {
                builder.RegisterScreens(region, registration => Register(registration, screens, backend));
            }
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new Page(0))
            .Child(left, Destination.For(new Page(1))).Child(right, Destination.For(new Page(2))));
        RegionInstanceId leftInstance = host.State.Current.Regions.Values.Single(region => region.DefinitionId == left).Id;
        Screen unchangedRight = screens.Single(screen => screen.RouteId == 2);
        ScreenForegroundToken rightForeground = Assert.Single(unchangedRight.Foregrounds);
        await host.Client.PushAsync(leftInstance, Destination.For(new Page(3)));
        Screen newLeft = screens.Single(screen => screen.RouteId == 3);

        Assert.True(unchangedRight.View.Presentation.Order > newLeft.View.Presentation.Order);
        Assert.True(unchangedRight.View.Presentation.InputEnabled);
        Assert.True(newLeft.View.Presentation.InputEnabled);
        Assert.False(screens.Single(screen => screen.RouteId == 1).View.Presentation.InputEnabled);
        Assert.Single(unchangedRight.Foregrounds);
        Assert.Single(unchangedRight.Activities);
        Assert.Equal(0, unchangedRight.Deactivations);
        Assert.True(rightForeground.IsValid);
        Assert.True(Assert.Single(newLeft.Foregrounds).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Visible_lower_screen_keeps_its_blocker_until_foreground_departure_or_shutdown (bool shutdown)
    {
        Backend backend = new();
        List<Screen> screens = new();
        Blocker blocker = new(backend);
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions
        {
            DefaultBlocker = new BlockerDefinition((preparation, _) => new ValueTask<IBlockerPresenter>(preparation.Lifetime.CreateOwned(() => blocker)))
        });
        await host.StartAsync(new Page(0));
        Screen first = Assert.Single(screens);
        ScreenActivityContext activity = Assert.Single(first.Activities);
        backend.Available = screen =>
        {
            if (screen.RouteId == 1)
            {
                Assert.False(first.View.Presentation.InputEnabled);
                Assert.True(blocker.View.Presentation.OutputEnabled);
                Assert.True(blocker.View.Presentation.InputEnabled);
                Assert.InRange(blocker.View.Presentation.Order, first.View.Presentation.Order + 1, screen.View.Presentation.Order - 1);
            }
        };

        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));

        Assert.True(activity.CancellationToken.IsCancellationRequested);
        Assert.True(blocker.View.Presentation.InputEnabled);
        Assert.True(Assert.Single(screens[1].Foregrounds).IsValid);
        ScreenForegroundToken popupForeground = screens[1].Foregrounds[0];
        bool observedBlockerClosure = false;
        backend.BlockerClosing = () =>
        {
            observedBlockerClosure = true;
            Assert.False(popupForeground.IsValid);
        };
        if (shutdown)
        {
            await host.ShutdownAsync();
        }
        else
        {
            await host.Client.BackAsync(host.Root);
        }
        Assert.True(observedBlockerClosure);
        backend.BlockerClosing = null;
        await host.ShutdownAsync();
        Assert.False(blocker.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Failed_preparation_restores_a_fresh_activation_and_foreground_period ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        Screen first = Assert.Single(screens);
        ScreenForegroundToken old = Assert.Single(first.Foregrounds);
        backend.Failure = "preparation";

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(host.Root, Destination.For(new Page(1))));

        Assert.False(failure.DestinationCommitted);
        Assert.False(old.IsValid);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.Equal(2, first.Activities.Count);
        Assert.Equal(2, first.Foregrounds.Count);
        Assert.True(first.Foregrounds[1].IsValid);
        Assert.Empty(screens[1].Foregrounds);
        Assert.Equal(1, first.Initializations);
        Assert.Equal(1, first.Preparations);
        Assert.Equal(1, first.Deactivations);
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("publication")]
    public async Task Completion_failure_leaves_no_valid_foreground_period (string stage)
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        backend.Failure = stage;

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(host.Root, Destination.For(new Page(1))));

        Assert.True(failure.DestinationCommitted);
        Assert.All(screens, screen => Assert.False(screen.View.Presentation.InputEnabled));
        Assert.All(screens.SelectMany(screen => screen.Foregrounds), foreground => Assert.False(foreground.IsValid));
        Assert.Equal(stage == "activation" ? 1 : 0, screens[1].Foregrounds.Count);
        Assert.Equal(stage == "activation" ? 1 : 0, screens[1].Deactivations);
    }

    [Fact]
    public async Task Cancellation_notification_failure_still_closes_native_input_and_restores_a_new_period ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        Screen first = Assert.Single(screens);
        ScreenForegroundToken old = Assert.Single(first.Foregrounds);
        using CancellationTokenRegistration registration = old.CancellationToken.Register(() => throw new InvalidOperationException("Application cancellation failed."));

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(host.Root, Destination.For(new Page(1))));

        Assert.False(failure.DestinationCommitted);
        Assert.False(old.IsValid);
        Assert.Equal(1, first.Deactivations);
        Assert.Equal(2, first.Foregrounds.Count);
        Assert.True(first.Foregrounds[1].IsValid);
        Assert.True(first.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Activation_is_a_lifecycle_callback_not_a_reentrant_navigation_entry ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        backend.Available = screen => Assert.Throws<InvalidOperationException>(() =>
            screen.Activities[^1].Navigation.Push(Destination.For(new Page(1))));

        await host.StartAsync(new Page(0));

        Assert.True(Assert.Single(Assert.Single(screens).Foregrounds).IsValid);
        Assert.Single(host.State.Current.GetRegion(host.Root).Entries);
    }

    [Fact]
    public async Task Native_loss_revokes_foreground_before_screen_shutdown ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        Screen screen = Assert.Single(screens);
        ScreenForegroundToken foreground = Assert.Single(screen.Foregrounds);

        screen.View.Lose();

        Assert.False(foreground.IsValid);
        Assert.True(foreground.CancellationToken.IsCancellationRequested);
        await host.ShutdownAsync();
        Assert.Equal(1, screen.Deactivations);
        Assert.Single(screen.Foregrounds);
    }

    [Fact]
    public async Task Popup_cover_ends_operation_subscriptions_without_ending_display_updates_or_started_work ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        await using NavigationHost host = CreateHost(screens, backend);
        await host.StartAsync(new Page(0));
        Screen home = screens[0];
        TaskCompletionSource<bool> started = Signal();
        TaskCompletionSource<bool> finish = Signal();
        CancellationToken workLifetime = default;
        ScreenWork work = home.Activities[0].StartWork(async context =>
        {
            workLifetime = context.CancellationToken;
            started.SetResult(true);
            await finish.Task.WaitAsync(context.CancellationToken);
        });
        Task completion = work.WaitAsync();
        await started.Task.WaitAsync(Timeout);
        backend.RequestOperation();
        Assert.Equal(1, home.Operations);

        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        backend.UpdateDisplay();
        backend.RequestOperation();

        Assert.True(home.View.Presentation.OutputEnabled);
        Assert.Equal(1, home.DisplayUpdates);
        Assert.Equal(1, home.Operations);
        Assert.Equal(1, home.Deactivations);
        Assert.False(workLifetime.IsCancellationRequested);
        Assert.False(completion.IsCompleted);
        Assert.Equal(1, screens[1].Operations);

        await host.Client.BackAsync(host.Root);
        backend.UpdateDisplay();
        backend.RequestOperation();
        Assert.Equal(2, home.DisplayUpdates);
        Assert.Equal(2, home.Operations);
        Assert.Equal(2, home.Activities.Count);
        Assert.Equal(1, home.Initializations);
        Assert.Equal(1, home.Preparations);
        Assert.Equal(ScreenActivationReason.HistoryReturn, home.Activities[1].Reason);
        finish.SetResult(true);
        await completion.WaitAsync(Timeout);
        await host.ShutdownAsync();
        backend.UpdateDisplay();
        backend.RequestOperation();
        Assert.Equal(2, home.DisplayUpdates);
        Assert.Equal(2, home.Operations);
        Assert.Equal(2, home.Deactivations);
    }

    [Fact]
    public async Task Native_focus_loss_observes_revoked_qualification_before_cancellation_notification ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        await using NavigationHost host = CreateHost(screens, backend);
        await host.StartAsync(new Page(0));
        Screen home = screens[0];
        ScreenForegroundToken token = home.Foregrounds[0];
        bool observed = false;
        backend.NativeClosing = view =>
        {
            if (!ReferenceEquals(view, home.View))
            {
                return;
            }
            observed = true;
            backend.NativeClosing = null;
            Assert.False(token.IsValid);
            Assert.False(token.CancellationToken.IsCancellationRequested);
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = home.Activities[0].Navigation.InvokeAsync(new Page(2));
            });
            backend.RequestOperation();
            Assert.Equal(0, home.Operations);
        };

        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));

        Assert.True(observed);
        Assert.True(token.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, home.Deactivations);
        backend.NativeClosing = null;
    }

    [Fact]
    public async Task A_cover_waits_for_deactivation_with_input_and_qualification_already_closed ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        await using NavigationHost host = CreateHost(screens, backend);
        await host.StartAsync(new Page(0));
        Screen home = screens[0];
        ScreenForegroundToken token = home.Foregrounds[0];
        TaskCompletionSource<bool> entered = Signal();
        TaskCompletionSource<bool> finish = Signal();
        backend.Deactivating = screen =>
        {
            if (!ReferenceEquals(screen, home))
            {
                return default;
            }
            entered.TrySetResult(true);
            return new ValueTask(finish.Task);
        };
        Task push = host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        try
        {
            await entered.Task.WaitAsync(Timeout);
            Assert.False(token.IsValid);
            Assert.False(home.View.Presentation.InputEnabled);
            Assert.True(home.View.Presentation.OutputEnabled);
            Assert.False(push.IsCompleted);
            Assert.DoesNotContain(screens, screen => screen.RouteId == 1 && screen.Foregrounds.Count != 0);
        }
        finally
        {
            finish.TrySetResult(true);
        }
        await push.WaitAsync(Timeout);
        await host.Client.BackAsync(host.Root);
        Assert.Equal(2, home.Activities.Count);
        Assert.Equal(1, home.Deactivations);
    }

    [Fact]
    public async Task Activation_waits_for_asynchronous_native_admission ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        await using NavigationHost host = CreateHost(screens, backend);
        await host.StartAsync(new Page(0));
        TaskCompletionSource<bool> applied = Signal();
        TaskCompletionSource<bool> finish = Signal();
        backend.AfterApply = changes =>
        {
            if (!changes.Changes.Any(change => change.Presentation.InputEnabled))
            {
                return default;
            }
            applied.TrySetResult(true);
            return new ValueTask(finish.Task);
        };
        Task push = host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        try
        {
            await applied.Task.WaitAsync(Timeout);
            Screen popup = screens.Single(screen => screen.RouteId == 1);
            Assert.Empty(popup.Activities);
            Assert.Empty(popup.Foregrounds);
            Assert.False(screens[0].Foregrounds[0].IsValid);
            backend.RequestOperation();
            Assert.Equal(0, popup.Operations);
            Assert.False(push.IsCompleted);
        }
        finally
        {
            finish.TrySetResult(true);
        }
        await push.WaitAsync(Timeout);
        Assert.True(Assert.Single(screens[1].Foregrounds).IsValid);
        backend.AfterApply = null;
    }

    [Fact]
    public async Task Nested_popups_repeat_foreground_periods_without_reinitializing_retained_screens ()
    {
        Backend backend = new();
        List<Screen> screens = new();
        await using NavigationHost host = CreateHost(screens, backend);
        await host.StartAsync(new Page(0));
        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        await host.Client.PushAsync(host.Root, Destination.For(new Page(2)));
        Assert.Equal(1, screens[0].Deactivations);
        Assert.Equal(1, screens[1].Deactivations);
        Assert.True(screens[0].View.Presentation.OutputEnabled);
        Assert.True(screens[1].View.Presentation.OutputEnabled);
        await host.Client.BackAsync(host.Root);
        backend.RequestOperation();
        Assert.Equal(0, screens[0].Operations);
        Assert.Equal(1, screens[1].Operations);
        await host.Client.BackAsync(host.Root);
        backend.RequestOperation();
        Assert.Equal(1, screens[0].Operations);
        Assert.Equal(1, screens[1].Operations);
        await host.ShutdownAsync();
        Assert.All(screens, screen => Assert.Equal(1, screen.Initializations));
        Assert.All(screens, screen => Assert.Equal(1, screen.Preparations));
        Assert.All(screens, screen => Assert.Equal(screen.Activities.Count, screen.Deactivations));
    }

    private static NavigationHost CreateHost (List<Screen> screens, Backend backend, Blocker? blocker = null)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        return NavigationHost.Create(catalog, new NavigationHostOptions
        {
            DefaultBlocker = blocker is null ? null : new BlockerDefinition((preparation, _) =>
                new ValueTask<IBlockerPresenter>(preparation.Lifetime.CreateOwned(() => blocker)))
        });
    }

    private static TaskCompletionSource<bool> Signal () => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Register (RegionScreenCatalogBuilder region, List<Screen> screens, Backend backend)
        => region.RegisterScreen(new ScreenDefinition<Page>((creation, _) =>
        {
            Screen screen = creation.Lifetime.CreateOwned(() => new Screen(backend));
            screens.Add(screen);
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }));
            return new ValueTask<IScreenLifecycleHandler<Page>>(screen);
        }, ScreenInstancePolicy.Multiple));

    private sealed record Page (int Id) : Route;

    private sealed class Screen : IScreenLifecycleHandler<Page>, IDisposable
    {
        private readonly Backend backend;
        public Screen (Backend backend)
        {
            this.backend = backend;
            View = new View(backend);
        }
        public View View { get; }
        public int RouteId { get; private set; }
        public bool ClosedDuringDeactivation { get; private set; }
        public List<ScreenActivityContext> Activities { get; } = new();
        public List<ScreenForegroundToken> Foregrounds { get; } = new();
        public int Initializations { get; private set; }
        public int Preparations { get; private set; }
        public int Deactivations { get; private set; }
        public int Operations { get; private set; }
        public int DisplayUpdates { get; private set; }
        public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
        {
            Initializations++;
            backend.DisplayChanged += UpdateDisplay;
            return default;
        }
        public ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            RouteId = route.Id;
            Preparations++;
            Assert.False(View.Presentation.InputEnabled);
            View.RejectOpening = RouteId == 1 && backend.Failure == "publication";
            if (RouteId == 1 && backend.Failure == "preparation")
            {
                throw new InvalidOperationException("Preparation failed.");
            }
            return default;
        }
        public ValueTask ActivateAsync (Page route, ScreenActivityContext activity)
        {
            Assert.True(View.Presentation.OutputEnabled);
            Assert.True(View.Presentation.InputEnabled);
            Assert.True(activity.ForegroundToken.IsValid);
            Activities.Add(activity);
            Foregrounds.Add(activity.ForegroundToken);
            backend.OperationRequested += Operate;
            backend.Available?.Invoke(this);
            if (RouteId == 1 && backend.Failure == "activation")
            {
                throw new InvalidOperationException("Activation failed.");
            }
            return backend.Activating?.Invoke(this) ?? default;
        }
        public ValueTask DeactivateAsync ()
        {
            Deactivations++;
            backend.OperationRequested -= Operate;
            ClosedDuringDeactivation = !View.IsAlive || !View.Presentation.InputEnabled;
            Assert.True(ClosedDuringDeactivation);
            Assert.All(Foregrounds, foreground => Assert.False(foreground.IsValid));
            return backend.Deactivating?.Invoke(this) ?? default;
        }
        public ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            Assert.False(View.IsAlive && View.Presentation.InputEnabled);
            Assert.All(Foregrounds, foreground => Assert.False(foreground.IsValid));
            return default;
        }
        private void UpdateDisplay () => DisplayUpdates++;
        private void Operate ()
        {
            if (Foregrounds.LastOrDefault().IsValid)
            {
                Operations++;
            }
        }
        public void Dispose () => backend.DisplayChanged -= UpdateDisplay;
    }

    private sealed class View : IViewPresentationBatchAdapter
    {
        public View (Backend backend) => PresentationBatch = backend;
        public IViewPresentationBatch PresentationBatch { get; }
        public object Identity { get; } = new();
        public object OrderingDomain => PresentationBatch;
        public bool IsAlive { get; private set; } = true;
        public bool RejectOpening { get; set; }
        public ViewPresentation Presentation { get; private set; }
        public event Action<string>? Lost;
        public void Validate (ViewPresentation presentation)
        {
            if (RejectOpening && presentation.InputEnabled)
            {
                throw new InvalidOperationException("Native input publication failed.");
            }
        }
        public void Lose ()
        {
            IsAlive = false;
            Lost?.Invoke("Native view lost.");
        }
        public void Apply (ViewPresentation presentation)
        {
            Assert.Equal(Presentation.InputEnabled, presentation.InputEnabled);
            Presentation = presentation;
        }
        public void Publish (ViewPresentation presentation) => Presentation = presentation;
    }

    private sealed class Backend : IViewPresentationBatch
    {
        public string? Failure { get; set; }
        public Action<Screen>? Available { get; set; }
        public Func<Screen, ValueTask>? Activating { get; set; }
        public Action<View>? NativeClosing { get; set; }
        public Action? BlockerClosing { get; set; }
        public Func<Screen, ValueTask>? Deactivating { get; set; }
        public Func<ViewPresentationChangeSet, ValueTask>? AfterApply { get; set; }
        public event Action? DisplayChanged;
        public event Action? OperationRequested;
        public void UpdateDisplay () => DisplayChanged?.Invoke();
        public void RequestOperation () => OperationRequested?.Invoke();
        public ValueTask ApplyAsync (ViewPresentationChangeSet changes)
        {
            foreach (ViewPresentationChange change in changes.Changes)
            {
                View view = (View)change.View;
                bool closing = view.Presentation.InputEnabled && !change.Presentation.InputEnabled;
                view.Publish(change.Presentation);
                if (closing)
                {
                    NativeClosing?.Invoke(view);
                }
            }
            return AfterApply?.Invoke(changes) ?? default;
        }
    }

    private sealed class Blocker : IBlockerPresenter, IDisposable
    {
        public Blocker (Backend backend) => View = new BlockerView(backend);
        public BlockerView View { get; }
        public ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken)
        {
            preparation.RegisterViewAdapter(View);
            return default;
        }
        public ValueTask TerminateAsync () => default;
        public void SetScreenContext (BlockerScreenContext? context) { }
        public void Dispose () { }
    }

    private sealed class BlockerView : IViewAdapter
    {
        private readonly Backend backend;
        public BlockerView (Backend backend)
        {
            this.backend = backend;
            OrderingDomain = backend;
        }
        public object Identity { get; } = new();
        public object OrderingDomain { get; }
        public bool IsAlive => true;
        public ViewPresentation Presentation { get; private set; }
        public event Action<string>? Lost { add { } remove { } }
        public void Validate (ViewPresentation presentation) { }
        public void Apply (ViewPresentation presentation)
        {
            bool closing = Presentation.InputEnabled && !presentation.InputEnabled;
            Presentation = presentation;
            if (closing)
            {
                backend.BlockerClosing?.Invoke();
            }
        }
    }
}
