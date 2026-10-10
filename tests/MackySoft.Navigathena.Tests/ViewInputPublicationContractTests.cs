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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Layered_push_closes_background_input_and_reopens_it_on_return (bool retainActivity)
    {
        Backend backend = new();
        List<Screen> screens = new();
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered,
            region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push,
                retainActivity ? LowerPresentationPolicy.Preserve : LowerPresentationPolicy.SuspendActivity));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        Screen first = Assert.Single(screens);
        Assert.True(first.View.Presentation.InputEnabled);
        ScreenActivityContext activity = Assert.Single(first.Activities);
        ScreenForegroundContext initialForeground = Assert.Single(first.Foregrounds);
        Assert.True(initialForeground.IsValid);

        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        Screen second = screens[1];
        Assert.True(first.View.Presentation.OutputEnabled);
        Assert.False(first.View.Presentation.InputEnabled);
        Assert.True(second.View.Presentation.InputEnabled);
        Assert.False(initialForeground.IsValid);
        Assert.True(initialForeground.CancellationToken.IsCancellationRequested);
        Assert.Equal(!retainActivity, activity.CancellationToken.IsCancellationRequested);
        if (!retainActivity)
        {
            Assert.True(first.ClosedDuringDeactivation);
        }

        await host.Client.BackAsync(host.Root);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.False(second.View.Presentation.InputEnabled);
        Assert.Equal(retainActivity ? 1 : 2, first.Activities.Count);
        Assert.Equal(2, first.Foregrounds.Count);
        Assert.True(first.Foregrounds[1].IsValid);
        Assert.False(initialForeground.IsValid);
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
        ScreenForegroundContext rightForeground = Assert.Single(unchangedRight.Foregrounds);
        await host.Client.PushAsync(leftInstance, Destination.For(new Page(3)));
        Screen newLeft = screens.Single(screen => screen.RouteId == 3);

        Assert.True(unchangedRight.View.Presentation.Order > newLeft.View.Presentation.Order);
        Assert.True(unchangedRight.View.Presentation.InputEnabled);
        Assert.True(newLeft.View.Presentation.InputEnabled);
        Assert.False(screens.Single(screen => screen.RouteId == 1).View.Presentation.InputEnabled);
        Assert.Same(rightForeground, Assert.Single(unchangedRight.Foregrounds));
        Assert.True(rightForeground.IsValid);
        Assert.True(Assert.Single(newLeft.Foregrounds).IsValid);
    }

    [Fact]
    public async Task Continuing_lower_activity_keeps_its_blocker_and_publishes_it_before_foreground_notification ()
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

        Assert.False(activity.CancellationToken.IsCancellationRequested);
        Assert.True(Assert.Single(screens[1].Foregrounds).IsValid);
        await host.ShutdownAsync();
        Assert.False(blocker.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Failed_preparation_restores_a_fresh_foreground_without_restarting_retained_activity ()
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
        ScreenForegroundContext old = Assert.Single(first.Foregrounds);
        backend.Failure = "preparation";

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(host.Root, Destination.For(new Page(1))));

        Assert.False(failure.DestinationCommitted);
        Assert.False(old.IsValid);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.Single(first.Activities);
        Assert.Equal(2, first.Foregrounds.Count);
        Assert.True(first.Foregrounds[1].IsValid);
        Assert.Empty(screens[1].Foregrounds);
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("publication")]
    [InlineData("foreground")]
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
        Assert.Equal(stage == "foreground" ? 1 : 0, screens[1].Foregrounds.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreground_closure_failure_still_closes_native_input_and_restores_a_new_period (bool notificationFails)
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
        ScreenForegroundContext old = Assert.Single(first.Foregrounds);
        using CancellationTokenRegistration registration = notificationFails ? default
            : old.CancellationToken.Register(() => throw new InvalidOperationException("Application cancellation failed."));
        if (notificationFails)
        {
            backend.Unavailable = _ =>
            {
                backend.Unavailable = null;
                throw new InvalidOperationException("Application closure callback failed.");
            };
        }

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(host.Root, Destination.For(new Page(1))));

        Assert.False(failure.DestinationCommitted);
        Assert.False(old.IsValid);
        Assert.Equal(1, first.ForegroundClosures);
        Assert.Equal(2, first.Foregrounds.Count);
        Assert.True(first.Foregrounds[1].IsValid);
        Assert.True(first.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Foreground_notification_is_a_lifecycle_callback_not_a_reentrant_navigation_entry ()
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
        ScreenForegroundContext foreground = Assert.Single(screen.Foregrounds);

        screen.View.Lose();

        Assert.False(foreground.IsValid);
        Assert.True(foreground.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, screen.ForegroundClosures);
        await host.ShutdownAsync();
        Assert.Single(screen.Foregrounds);
    }

    private static void Register (RegionScreenCatalogBuilder region, List<Screen> screens, Backend backend)
        => region.RegisterScreen(new ScreenDefinition<Page>((creation, _) =>
        {
            Screen screen = creation.Lifetime.CreateOwned(() => new Screen(backend));
            screens.Add(screen);
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }));
            return new ValueTask<IScreenLifecycleHandler<Page>>(screen);
        }, ScreenInstancePolicy.Multiple));

    private sealed record Page (int Id) : Route;

    private sealed class Screen : IScreenLifecycleHandler<Page>, IScreenForegroundLifecycleHandler, IDisposable
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
        public List<ScreenForegroundContext> Foregrounds { get; } = new();
        public int ForegroundClosures { get; private set; }
        public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            RouteId = route.Id;
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
            Assert.False(View.Presentation.InputEnabled);
            Activities.Add(activity);
            if (RouteId == 1 && backend.Failure == "activation")
            {
                throw new InvalidOperationException("Activation failed.");
            }
            return default;
        }
        public ValueTask DeactivateAsync ()
        {
            ClosedDuringDeactivation = !View.IsAlive || !View.Presentation.InputEnabled;
            Assert.True(ClosedDuringDeactivation);
            Assert.All(Foregrounds, foreground => Assert.False(foreground.IsValid));
            return default;
        }
        public ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            Assert.False(View.IsAlive && View.Presentation.InputEnabled);
            Assert.All(Foregrounds, foreground => Assert.False(foreground.IsValid));
            return default;
        }
        public void OnForegroundAvailable (ScreenForegroundContext foreground)
        {
            Assert.True(View.Presentation.OutputEnabled);
            Assert.True(View.Presentation.InputEnabled);
            Assert.True(foreground.IsValid);
            Foregrounds.Add(foreground);
            backend.Available?.Invoke(this);
            if (RouteId == 1 && backend.Failure == "foreground")
            {
                throw new InvalidOperationException("Foreground callback failed.");
            }
        }
        public void OnForegroundUnavailable ()
        {
            Assert.False(Foregrounds[^1].IsValid);
            if (View.IsAlive)
            {
                Assert.False(View.Presentation.InputEnabled);
            }
            ForegroundClosures++;
            backend.Unavailable?.Invoke(this);
        }
        public void Dispose () { }
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
        public Action<Screen>? Unavailable { get; set; }
        public ValueTask ApplyAsync (ViewPresentationChangeSet changes)
        {
            foreach (ViewPresentationChange change in changes.Changes)
            {
                ((View)change.View).Publish(change.Presentation);
            }
            return default;
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
        public BlockerView (Backend backend) => OrderingDomain = backend;
        public object Identity { get; } = new();
        public object OrderingDomain { get; }
        public bool IsAlive => true;
        public ViewPresentation Presentation { get; private set; }
        public event Action<string>? Lost { add { } remove { } }
        public void Validate (ViewPresentation presentation) { }
        public void Apply (ViewPresentation presentation) => Presentation = presentation;
    }
}
