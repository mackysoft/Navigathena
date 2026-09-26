using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenPresentationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task Independent_regions_keep_their_foreground_and_follow_a_covered_parent ()
    {
        RegionDefinitionId left = new("left");
        RegionDefinitionId right = new("right");
        List<Screen> screens = new();
        static void Configure (RouteDefinitionBuilder<Popup> route)
        {
            route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
        }
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<Popup>(route =>
        {
            Configure(route);
            route.AddChildRegion(left, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<Popup>(Configure));
            route.AddChildRegion(right, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<Popup>(Configure));
        }));
        ScreenDefinition<Popup> screenDefinition = new((creation, _) =>
        {
            Screen screen = creation.Lifetime.CreateOwned(() => new Screen());
            screens.Add(screen);
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }, screen));
            return new ValueTask<IScreenLifecycleHandler<Popup>>(screen);
        }, ScreenInstancePolicy.Multiple);
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
        {
            foreach (RegionDefinitionId region in new[] { Root, left, right })
            {
                builder.RegisterScreens(region, registrations => registrations.RegisterScreen(screenDefinition));
            }
        });
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(Destination.For(new Popup(0))
            .Child(left, Destination.For(new Popup(1)))
            .Child(right, Destination.For(new Popup(2))));
        Assert.All(screens, screen => Assert.Equal(ScreenAnimationState.Foreground, screen.State));

        RegionInstanceId leftInstance = host.State.Current.Regions.Values.Single(region => region.DefinitionId == left).Id;
        await host.Client.PushAsync(leftInstance, Destination.For(new Popup(3)));
        Screen leftPrevious = screens.Single(screen => screen.AnimatedInputs[0].Input == 1);
        Screen rightScreen = screens.Single(screen => screen.AnimatedInputs[0].Input == 2);
        Assert.Equal(ScreenAnimationState.Background, leftPrevious.State);
        Assert.Equal(ScreenAnimationState.Foreground, rightScreen.State);
        Assert.Single(rightScreen.Animations);

        await host.Client.PushAsync(host.Root, Destination.For(new Popup(4)));
        Assert.All(screens.Where(screen => screen.AnimatedInputs[0].Input != 4), screen => Assert.Equal(ScreenAnimationState.Background, screen.State));
        await host.Client.BackAsync(host.Root);
        Assert.Equal(ScreenAnimationState.Background, leftPrevious.State);
        Assert.Equal(ScreenAnimationState.Foreground, rightScreen.State);
        Assert.Equal(ScreenAnimationKind.Reveal, rightScreen.Animations.Last().Kind);
    }

    [Fact]
    public async Task Single_instance_finishes_old_content_animation_before_repreparing_for_push_and_back ()
    {
        List<Screen> screens = new();
        NavigationHost host = CreateHost(screens, policy: LowerPresentationPolicy.HideAndRetain, instancePolicy: ScreenInstancePolicy.Single);
        await host.StartAsync(new Popup(1));
        await host.Client.PushAsync(host.Root, Destination.For(new Popup(2)));
        await host.Client.BackAsync(host.Root);
        Screen screen = Assert.Single(screens);
        Assert.Equal(new[]
        {
            (ScreenAnimationKind.Enter, 1),
            (ScreenAnimationKind.Cover, 1),
            (ScreenAnimationKind.Enter, 2),
            (ScreenAnimationKind.Exit, 2),
            (ScreenAnimationKind.Reveal, 1)
        }, screen.AnimatedInputs);
        Assert.Equal(3, screen.Preparations);
        Assert.Equal(0, screen.Disposals);
        await host.ShutdownAsync();
        Assert.Equal(1, screen.Disposals);
    }

    [Fact]
    public async Task Visible_layers_animate_cover_and_reveal_only_when_their_position_changes ()
    {
        List<Screen> screens = new();
        NavigationHost host = CreateHost(screens);
        await host.StartAsync(new Popup(1));
        await host.Client.PushAsync(host.Root, Destination.For(new Popup(2)));
        await host.Client.PushAsync(host.Root, Destination.For(new Popup(3)));
        Assert.Equal(new[] { ScreenAnimationKind.Enter, ScreenAnimationKind.Cover }, screens[0].Animations.Select(item => item.Kind));
        Assert.Equal(ScreenAnimationState.Background, screens[0].State);
        Assert.True(screens[0].View.Presentation.OutputEnabled);
        Assert.False(screens[0].View.Presentation.InputEnabled);

        await host.Client.BackAsync(host.Root);
        Assert.Equal(ScreenAnimationKind.Exit, screens[2].Animations.Last().Kind);
        Assert.Equal(ScreenAnimationState.AfterExit, screens[2].State);
        Assert.Equal(1, screens[2].Disposals);
        Assert.Equal(ScreenAnimationKind.Reveal, screens[1].Animations.Last().Kind);
        Assert.Equal(2, screens[0].Animations.Count);

        await host.Client.BackAsync(host.Root);
        Assert.Equal(ScreenAnimationKind.Reveal, screens[0].Animations.Last().Kind);
        Assert.Equal(ScreenAnimationState.Foreground, screens[0].State);
        Assert.True(screens[0].View.Presentation.InputEnabled);
        Assert.Equal(1, screens[0].Preparations);
        await host.ShutdownAsync();
        Assert.All(screens, screen => Assert.Equal(1, screen.Disposals));
    }

    [Fact]
    public async Task Skipping_return_animation_applies_foreground_without_repreparing ()
    {
        List<Screen> screens = new();
        NavigationHost host = CreateHost(screens);
        await host.StartAsync(new Popup(1));
        await host.Client.PushAsync(host.Root, Destination.For(new Popup(2)));
        await host.Client.BackAsync(host.Root, new BackOptions { EnterAnimation = ScreenEnterAnimationMode.Skip });
        Assert.Equal(2, screens[0].Animations.Count);
        Assert.Equal(ScreenAnimationState.Foreground, screens[0].State);
        Assert.Equal(1, screens[0].Preparations);
        await host.ShutdownAsync();
    }

    [Fact]
    public async Task Duplicate_primary_presentation_fails_before_publishing_and_releases_owned_resources_once ()
    {
        Screen screen = new();
        NavigationHost host = CreateHost(new List<Screen>(), creation =>
        {
            creation.Lifetime.CreateOwned(() => screen);
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }));
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { new View() }));
        });
        await Assert.ThrowsAnyAsync<NavigationException>(() => host.Start(new Popup(1)).WaitAsync());
        Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
        Assert.False(screen.View.Presentation.OutputEnabled);
        Assert.Equal(1, screen.Disposals);
        await host.ShutdownAsync();
    }

    [Fact]
    public async Task Invalid_multi_view_binding_does_not_mutate_any_view ()
    {
        View view = new();
        NavigationHost host = CreateHost(new List<Screen>(), creation =>
        {
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { view, view }));
        });
        await Assert.ThrowsAnyAsync<NavigationException>(() => host.Start(new Popup(1)).WaitAsync());
        Assert.Equal(0, view.Writes);
        await host.ShutdownAsync();
    }

    [Fact]
    public async Task Failure_cancels_sibling_playback_and_waits_until_its_writer_stops ()
    {
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource coverStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<Screen> screens = new();
        NavigationHost host = CreateHost(screens, onCreated: screen =>
        {
            if (screens.Count == 2)
            {
                screen.Play = async (_, _) =>
                {
                    await coverStarted.Task;
                    throw new InvalidOperationException("Animation failed.");
                };
            }
        });
        await host.StartAsync(new Popup(1));
        screens[0].Play = async (animation, token) =>
        {
            if (animation.Kind == ScreenAnimationKind.Cover)
            {
                coverStarted.TrySetResult();
                using CancellationTokenRegistration registration = token.Register(() => cancellationObserved.TrySetResult());
                await stopped.Task;
                token.ThrowIfCancellationRequested();
            }
        };
        Task transition = host.Client.Push(host.Root, Destination.For(new Popup(2))).WaitAsync();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(transition.IsCompleted);
        Assert.All(screens, screen => Assert.Equal(0, screen.Disposals));
        stopped.SetResult();
        await Assert.ThrowsAnyAsync<NavigationException>(() => transition);
        Assert.Equal(2, host.State.Current.GetRegion(host.Root).Entries.Count);
        Assert.All(screens, screen => Assert.False(screen.View.Presentation.InputEnabled));
        await host.ShutdownAsync();
        Assert.All(screens, screen => Assert.Equal(1, screen.Disposals));
    }

    private static NavigationHost CreateHost (List<Screen> screens, Action<ScreenCreationContext<Popup>>? configure = null, Action<Screen>? onCreated = null,
        LowerPresentationPolicy? policy = null, ScreenInstancePolicy instancePolicy = ScreenInstancePolicy.Multiple)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<Popup>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Reset | RouteEntryOperations.Replace;
                route.LowerPresentationPolicy = policy ?? LowerPresentationPolicy.BlockInput;
            });
        });
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, registrations =>
        {
            registrations.RegisterScreen(new ScreenDefinition<Popup>((creation, _) =>
            {
                configure?.Invoke(creation);
                Screen screen = creation.Lifetime.CreateOwned(() => new Screen());
                screens.Add(screen);
                onCreated?.Invoke(screen);
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }, screen));
                return new ValueTask<IScreenLifecycleHandler<Popup>>(screen);
            }, instancePolicy));
        }));
        return NavigationHost.Create(catalog);
    }

    private sealed record Popup (int Id) : Route;

    private sealed class Screen : IScreenLifecycleHandler<Popup>, IScreenAnimator, IDisposable
    {
        public View View { get; } = new();
        public List<ScreenAnimation> Animations { get; } = new();
        public List<(ScreenAnimationKind Kind, int Input)> AnimatedInputs { get; } = new();
        private int input;
        public ScreenAnimationState State
        {
            get; private set;
        }
        public int Disposals
        {
            get; private set;
        }
        public int Preparations
        {
            get; private set;
        }
        public Func<ScreenAnimation, CancellationToken, Task>? Play
        {
            get; set;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (Popup route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Preparations++;
            input = route.Id;
            return default;
        }
        public ValueTask ActivateAsync (Popup route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
        public void Dispose () => Disposals++;
        public void SetStateImmediately (ScreenAnimationState state) => State = state;
        public async ValueTask PlayAsync (ScreenAnimation animation, CancellationToken cancellationToken)
        {
            Animations.Add(animation);
            AnimatedInputs.Add((animation.Kind, input));
            if (Play is not null)
            {
                await Play(animation, cancellationToken);
            }
        }
    }

    private sealed class View : IViewAdapter
    {
        public object Identity { get; } = new();
        public object OrderingDomain => "test";
        public bool IsAlive => true;
        public ViewPresentation Presentation
        {
            get; private set;
        }
        public int Writes
        {
            get; private set;
        }
        public event Action<string>? Lost { add { } remove { } }
        public void Validate (ViewPresentation presentation)
        {
        }
        public void Apply (ViewPresentation presentation)
        {
            Writes++;
            Presentation = presentation;
        }
    }
}
