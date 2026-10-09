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
                retainActivity ? LowerPresentationPolicy.Preserve : LowerPresentationPolicy.BlockInput));
        ScreenCatalog catalog = ScreenCatalog.Build(definition,
            builder => builder.RegisterScreens(Root, region => Register(region, screens, backend)));
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(0));
        Screen first = Assert.Single(screens);
        Assert.True(first.View.Presentation.InputEnabled);

        await host.Client.PushAsync(host.Root, Destination.For(new Page(1)));
        Screen second = screens[1];
        Assert.True(first.View.Presentation.OutputEnabled);
        Assert.False(first.View.Presentation.InputEnabled);
        Assert.True(second.View.Presentation.InputEnabled);
        if (!retainActivity)
        {
            Assert.True(first.ClosedDuringDeactivation);
        }

        await host.Client.BackAsync(host.Root);
        Assert.True(first.View.Presentation.InputEnabled);
        Assert.False(second.View.Presentation.InputEnabled);
        await host.ShutdownAsync();
        Assert.All(screens, screen => Assert.False(screen.View.Presentation.InputEnabled));
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
        await host.Client.PushAsync(leftInstance, Destination.For(new Page(3)));
        Screen newLeft = screens.Single(screen => screen.RouteId == 3);
        Screen unchangedRight = screens.Single(screen => screen.RouteId == 2);

        Assert.True(unchangedRight.View.Presentation.Order > newLeft.View.Presentation.Order);
        Assert.True(unchangedRight.View.Presentation.InputEnabled);
        Assert.True(newLeft.View.Presentation.InputEnabled);
        Assert.False(screens.Single(screen => screen.RouteId == 1).View.Presentation.InputEnabled);
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

    private sealed class Screen : IScreenLifecycleHandler<Page>, IDisposable
    {
        public Screen (Backend backend) => View = new View(backend);
        public View View { get; }
        public int RouteId { get; private set; }
        public bool ClosedDuringDeactivation { get; private set; }
        public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            RouteId = route.Id;
            Assert.False(View.Presentation.InputEnabled);
            return default;
        }
        public ValueTask ActivateAsync (Page route, ScreenActivityContext activity)
        {
            Assert.False(View.Presentation.InputEnabled);
            return default;
        }
        public ValueTask DeactivateAsync ()
        {
            ClosedDuringDeactivation = !View.Presentation.InputEnabled;
            Assert.True(ClosedDuringDeactivation);
            return default;
        }
        public ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            Assert.False(View.Presentation.InputEnabled);
            return default;
        }
        public void Dispose () { }
    }

    private sealed class View : IViewPresentationBatchAdapter
    {
        public View (Backend backend) => PresentationBatch = backend;
        public IViewPresentationBatch PresentationBatch { get; }
        public object Identity { get; } = new();
        public object OrderingDomain => PresentationBatch;
        public bool IsAlive => true;
        public ViewPresentation Presentation { get; private set; }
        public event Action<string>? Lost { add { } remove { } }
        public void Validate (ViewPresentation presentation) { }
        public void Apply (ViewPresentation presentation)
        {
            Assert.Equal(Presentation.InputEnabled, presentation.InputEnabled);
            Presentation = presentation;
        }
        public void Publish (ViewPresentation presentation) => Presentation = presentation;
    }

    private sealed class Backend : IViewPresentationBatch
    {
        public ValueTask ApplyAsync (ViewPresentationChangeSet changes)
        {
            foreach (ViewPresentationChange change in changes.Changes)
            {
                ((View)change.View).Publish(change.Presentation);
            }
            return default;
        }
    }
}
