using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class RegionPresentationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Left = new("left");
    private static readonly RegionDefinitionId Right = new("right");
    private static readonly RegionDefinitionId Details = new("details");

    [Fact]
    public async Task Editing_hides_and_retains_the_hud_and_menu_then_back_restores_only_the_menu_activity ()
    {
        Fixture game = new();
        await using NavigationHost host = game.Host;
        await host.StartAsync(new Hud("game"));
        Screen hud = game.Current(host.Root);
        await hud.Activity.Navigation.PushAsync(new Popup());
        Screen menu = game.Current(host.Root);
        menu.Selection = 42;
        ScreenActivityContext menuActivity = menu.Activity;
        await menuActivity.Navigation.PushAsync(new Editor());
        Screen editor = game.Current(host.Root);

        Assert.Equal(3, host.State.Current.GetRegion(host.Root).Entries.Count);
        Assert.False(hud.View.Presentation.OutputEnabled);
        Assert.False(menu.View.Presentation.OutputEnabled);
        Assert.True(editor.View.Presentation.InputEnabled);
        Assert.True(menuActivity.CancellationToken.IsCancellationRequested);
        Assert.Equal(ScreenAnimationState.Hidden, menu.Animations.Last().To);
        Assert.Equal(0, hud.Disposals);
        Assert.Equal(0, menu.Disposals);

        await editor.Activity.Navigation.BackAsync();

        Assert.Same(menu, game.Current(host.Root));
        Assert.Equal(42, menu.Selection);
        Assert.Equal(1, menu.Preparations);
        Assert.Equal(2, menu.Activations);
        Assert.Equal(1, hud.Activations);
        Assert.True(hud.View.Presentation.OutputEnabled);
        Assert.False(hud.View.Presentation.InputEnabled);
        Assert.True(menu.View.Presentation.InputEnabled);
        Assert.Equal(ScreenAnimationKind.Reveal, menu.Animations.Last().Kind);
        Assert.Equal(1, editor.Disposals);
        await Assert.ThrowsAsync<NavigationException>(() => menuActivity.Navigation.PushAsync(new Editor()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Lower_policy_affects_only_its_region_lower_entries_and_their_children (int policy)
    {
        Fixture game = new();
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync(withDetails: true);
        RegionInstanceId left = game.Region(Left);
        Screen shell = game.Current(host.Root);
        Screen leftHud = game.Current(left);
        Screen rightHud = game.Current(game.Region(Right));
        Screen details = game.Current(game.Region(Details));
        leftHud.Selection = 42;
        Route route = policy switch
        {
            0 => new Popup(),
            1 => new Editor(),
            _ => new ReleaseEditor()
        };

        await leftHud.Activity.Navigation.PushAsync(route);

        Assert.True(shell.View.Presentation.InputEnabled);
        Assert.True(rightHud.View.Presentation.InputEnabled);
        Assert.False(shell.Activity.CancellationToken.IsCancellationRequested);
        Assert.False(rightHud.Activity.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, rightHud.Activations);
        Assert.Single(rightHud.Animations);
        Assert.False(leftHud.View.Presentation.InputEnabled);
        Assert.False(details.View.Presentation.InputEnabled);
        Assert.Equal(policy == 0, leftHud.View.Presentation.OutputEnabled);
        Assert.Equal(policy == 0, details.View.Presentation.OutputEnabled);
        Assert.Equal(policy == 2 ? 1 : 0, leftHud.Disposals);
        Assert.Equal(policy == 2 ? 1 : 0, details.Disposals);
        Assert.Equal(0, shell.Disposals);
        Assert.Equal(0, rightHud.Disposals);

        await game.Current(left).Activity.Navigation.BackAsync();

        Screen restored = game.Current(left);
        Assert.Equal(42, restored.Selection);
        Assert.True(restored.View.Presentation.InputEnabled);
        Assert.True(game.Current(game.Region(Details)).View.Presentation.InputEnabled);
        Assert.Equal(policy == 2, !ReferenceEquals(leftHud, restored));
        Assert.Single(rightHud.Animations);
        Assert.Equal(1, shell.Activations);
    }

    [Fact]
    public async Task Distinct_blocker_definitions_allow_independent_connections_and_parent_cover_hides_both_until_back ()
    {
        Fixture game = new(independentBlockers: true);
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        await host.Client.PushAsync(left, Destination.For(new Popup()));
        await host.Client.PushAsync(right, Destination.For(new Popup()));
        Blocker leftBlocker = game.Blockers.Single(blocker => blocker.Region == left);
        Blocker rightBlocker = game.Blockers.Single(blocker => blocker.Region == right);
        BlockerScreenContext rightConnection = rightBlocker.Context!;
        Assert.All(game.Blockers, blocker => Assert.True(blocker.View.Presentation.InputEnabled));
        Assert.Equal(right, rightConnection.RegionId);

        await host.Client.PushAsync(left, Destination.For(new Editor()));
        Assert.False(leftBlocker.View.Presentation.OutputEnabled);
        Assert.True(rightBlocker.View.Presentation.InputEnabled);
        Assert.Same(rightConnection, rightBlocker.Context);
        await host.Client.BackAsync(left);
        Assert.True(leftBlocker.View.Presentation.InputEnabled);
        Assert.Equal(2, game.Blockers.Count);

        await host.Client.PushAsync(host.Root, Destination.For(new Editor()));
        Assert.All(game.Blockers, blocker => Assert.False(blocker.View.Presentation.OutputEnabled));
        Assert.All(game.Blockers, blocker => Assert.Equal(0, blocker.Disposals));
        await host.Client.BackAsync(host.Root);
        Assert.All(game.Blockers, blocker => Assert.True(blocker.View.Presentation.InputEnabled));
        Assert.Equal(2, game.Blockers.Count);

        await host.Client.ResetAsync(host.Root, Destination.For(new Hud("standalone")));
        Assert.All(game.Blockers, blocker => Assert.Equal(0, blocker.Disposals));
        await host.ShutdownAsync();
        Assert.All(game.Blockers, blocker => Assert.Equal(1, blocker.Disposals));
    }

    [Fact]
    public async Task Sibling_can_open_a_popup_while_another_regions_cover_animation_is_running ()
    {
        Fixture game = new(independentBlockers: true);
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        await host.Client.PushAsync(left, Destination.For(new Popup()));
        Blocker leftBlocker = Assert.Single(game.Blockers);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Play = async (screen, _) =>
        {
            if (screen.Route is Editor)
            {
                entered.TrySetResult();
                await release.Task;
            }
        };
        Task editing = host.Client.PushAsync(left, Destination.For(new Editor()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await host.Client.PushAsync(right, Destination.For(new Popup())).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(editing.IsCompleted);
            Assert.False(leftBlocker.View.Presentation.OutputEnabled);
            Assert.True(game.Blockers.Single(blocker => blocker.Region == right).View.Presentation.InputEnabled);
        }
        finally
        {
            release.TrySetResult();
            await editing;
        }
        Assert.True(game.Blockers.Single(blocker => blocker.Region == right).View.Presentation.InputEnabled);
        Assert.Equal(0, game.Blockers.Single(blocker => blocker.Region == right).Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_local_preparation_restores_its_source_without_interrupting_the_sibling (bool failValidation)
    {
        Fixture game = new(independentBlockers: true);
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        await host.Client.PushAsync(left, Destination.For(new Popup()));
        await host.Client.PushAsync(right, Destination.For(new Popup()));
        Screen source = game.Current(left);
        Screen sibling = game.Current(right);
        Blocker siblingBlocker = game.Blockers.Single(blocker => blocker.Region == right);
        BlockerScreenContext connection = siblingBlocker.Context!;
        game.FailPrepare = screen => !failValidation && screen.Route is Editor;
        game.FailValidation = screen => failValidation && screen.Route is Editor;

        await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(left, Destination.For(new Editor())));

        Assert.Same(source, game.Current(left));
        Assert.True(source.View.Presentation.InputEnabled);
        Assert.True(game.Blockers.Single(blocker => blocker.Region == left).View.Presentation.InputEnabled);
        Assert.Same(connection, siblingBlocker.Context);
        Assert.True(siblingBlocker.View.Presentation.InputEnabled);
        Assert.False(sibling.Activity.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, sibling.Activations);
        Assert.Equal(0, sibling.Disposals);
    }

    [Fact]
    public async Task Failed_initial_publication_releases_blockers_of_uncommitted_regions ()
    {
        Fixture game = new(independentBlockers: true);
        await using NavigationHost host = game.Host;
        game.FailValidation = screen => screen.Route is Shell;

        await Assert.ThrowsAsync<NavigationException>(() => host.StartAsync(Destination.For(new Shell())
            .Child(Left, Destination.For(new Popup()))
            .Child(Right, Destination.For(new Popup()))));

        Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
        Assert.Equal(2, game.Blockers.Count);
        Assert.All(game.Blockers, blocker => Assert.Equal(1, blocker.Disposals));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_definition_reuses_one_blocker_across_regions_and_rejects_old_connections (bool registered)
    {
        Fixture game = new(sharedRegistration: registered);
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        await host.Client.PushAsync(left, Destination.For(new Popup()));
        Blocker blocker = Assert.Single(game.Blockers);
        BlockerScreenContext old = blocker.Context!;
        await host.Client.BackAsync(left);
        Assert.Null(blocker.Context);
        Assert.Equal(0, blocker.Disposals);

        await host.Client.PushAsync(right, Destination.For(new Popup()));

        Assert.Same(blocker, Assert.Single(game.Blockers));
        Assert.Equal(right, blocker.Context!.RegionId);
        Assert.True(blocker.View.Presentation.InputEnabled);
        Assert.True(game.Current(right).View.Presentation.Order > blocker.View.Presentation.Order);
        Assert.True(blocker.View.Presentation.Order > game.Current(left).View.Presentation.Order);
        Assert.Equal(NavigationResultKind.Rejected, (await old.Navigation.Back().WaitAsync()).Kind);
        await host.Client.ResetAsync(host.Root, Destination.For(new Hud("standalone")));
        Assert.Null(blocker.Context);
        Assert.Equal(0, blocker.Disposals);
        await host.ShutdownAsync();
        Assert.Equal(1, blocker.Disposals);
    }

    [Fact]
    public async Task Shared_blocker_rejects_simultaneous_connections_before_stopping_either_screen ()
    {
        Fixture game = new();
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        await host.Client.PushAsync(left, Destination.For(new Popup()));
        Screen leftScreen = game.Current(left);
        Screen rightScreen = game.Current(right);
        Blocker blocker = Assert.Single(game.Blockers);
        BlockerScreenContext connection = blocker.Context!;
        NavigationState before = host.State.Current;

        await Assert.ThrowsAsync<NavigationConfigurationException>(() => host.Client.PushAsync(right, Destination.For(new Popup())));

        Assert.Same(before, host.State.Current);
        Assert.Same(connection, blocker.Context);
        Assert.Single(game.Blockers);
        Assert.False(leftScreen.Activity.CancellationToken.IsCancellationRequested);
        Assert.False(rightScreen.Activity.CancellationToken.IsCancellationRequested);
        Assert.True(leftScreen.View.Presentation.InputEnabled);
        Assert.True(rightScreen.View.Presentation.InputEnabled);
        Assert.Equal(1, leftScreen.Activations);
        Assert.Equal(1, rightScreen.Activations);
    }

    [Fact]
    public async Task Shared_connection_remains_reserved_until_the_transition_finishes ()
    {
        Fixture game = new();
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        await host.Client.PushAsync(left, Destination.For(new Popup()));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Play = async (screen, _) =>
        {
            if (screen.Route is Editor)
            {
                entered.TrySetResult();
                await release.Task;
            }
        };
        Task editing = host.Client.PushAsync(left, Destination.For(new Editor()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            NavigationResult rejected = await host.Client.Push(right, Destination.For(new Popup())).WaitAsync();
            Assert.Equal(NavigationResultKind.Conflict, rejected.Kind);
            Assert.IsType<Hud>(game.Current(right).Route);
            Assert.False(game.Current(right).Activity.CancellationToken.IsCancellationRequested);
        }
        finally
        {
            release.TrySetResult();
            await editing;
        }
        await host.Client.PushAsync(right, Destination.For(new Popup()));
        Assert.Single(game.Blockers);
        Assert.Equal(right, game.Blockers.Single().Context!.RegionId);
    }

    [Fact]
    public async Task Parent_owned_blocker_ends_before_its_source_screen_and_is_rebuilt_with_the_next_parent ()
    {
        Fixture game = new(parentOwnedBlocker: true);
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        Screen parent = game.Current(host.Root);
        await host.Client.PushAsync(game.Region(Left), Destination.For(new Popup()));
        Blocker blocker = Assert.Single(game.Blockers);
        await host.Client.BackAsync(game.Region(Left));
        Assert.Equal(0, blocker.Disposals);
        await host.Client.ResetAsync(host.Root, Destination.For(new Hud("standalone")));
        Assert.Equal(1, blocker.Disposals);
        Assert.Equal(1, parent.Disposals);
        Assert.True(game.Endings.IndexOf(blocker) < game.Endings.IndexOf(parent));

        await host.Client.ResetAsync(host.Root, Destination.For(new Shell())
            .Child(Left, Destination.For(new Hud("left")))
            .Child(Right, Destination.For(new Hud("right"))));
        await host.Client.PushAsync(game.Region(Left), Destination.For(new Popup()));
        Assert.Equal(2, game.Blockers.Count);
        Assert.Single(game.Blockers, item => item.Disposals == 0);
    }

    [Fact]
    public async Task Ordering_keeps_outgoing_views_distinct_and_a_live_effect_above_concurrent_sibling_changes ()
    {
        Fixture game = new(independentBlockers: true);
        await using NavigationHost host = game.Host;
        await game.StartSplitAsync();
        RegionInstanceId left = game.Region(Left);
        RegionInstanceId right = game.Region(Right);
        Screen outgoing = game.Current(left);
        Effect effect = new();
        game.Play = async (screen, _) =>
        {
            if (ReferenceEquals(screen, outgoing))
            {
                await effect.Release.Task;
            }
        };
        NavigationTransition transition = new(NavigationTransitionScope.Region, (preparation, _) =>
        {
            preparation.RegisterViewAdapter(effect.View);
            return new ValueTask<INavigationTransitionEffect>(effect);
        });
        Task replacing = host.Client.ReplaceAsync(left, Destination.For(new Hud("replacement")), new NavigationOptions { Transition = transition });
        try
        {
            await effect.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await host.Client.PushAsync(right, Destination.For(new Popup()));
            Screen incoming = game.Current(left);
            Assert.NotSame(outgoing, incoming);
            Assert.True(outgoing.View.Presentation.OutputEnabled);
            Assert.True(incoming.View.Presentation.OutputEnabled);
            View[] visible = game.Screens.SelectMany(screen => new[] { screen.View, screen.FrontView })
                .Concat(game.Blockers.Select(blocker => blocker.View)).Where(view => view.Presentation.OutputEnabled).ToArray();
            Assert.Equal(visible.Length, visible.Select(view => view.Presentation.Order).Distinct().Count());
            Assert.All(visible, view => Assert.True(view.Presentation.Order < effect.View.Presentation.Order));
            Assert.True(incoming.View.Presentation.Order < incoming.FrontView.Presentation.Order);
        }
        finally
        {
            effect.Release.TrySetResult();
            await replacing;
        }
        Assert.False(effect.View.Presentation.OutputEnabled);
        Assert.Equal(1, outgoing.Disposals);
    }

    private sealed class Effect : INavigationTransitionEffect
    {
        public View View { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken) => default;
        public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
        public async ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
        }
        public ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken) => default;
    }

    private sealed record Shell : Route;
    private sealed record Hud (string Name) : Route;
    private sealed record Popup : Route;
    private sealed record Editor : Route;
    private sealed record ReleaseEditor : Route;

    private sealed class Fixture
    {
        public ConcurrentBag<Screen> Screens { get; } = new();
        public ConcurrentBag<Blocker> Blockers { get; } = new();
        public List<object> Endings { get; } = new();
        private readonly bool parentOwnedBlocker;
        public Func<Screen, ScreenAnimation, Task>? Play
        {
            get; set;
        }
        public Func<Screen, bool>? FailPrepare
        {
            get; set;
        }
        public Func<Screen, bool>? FailValidation
        {
            get; set;
        }
        public NavigationHost Host
        {
            get;
        }

        public Fixture (bool independentBlockers = false, bool sharedRegistration = false, bool parentOwnedBlocker = false)
        {
            this.parentOwnedBlocker = parentOwnedBlocker;
            BlockerDefinition common = DefineBlocker();
            NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            {
                root.AddRoute<Hud>(Configure);
                root.AddRoute<Popup>(Configure);
                root.AddRoute<Editor>(Configure);
                root.AddRoute<ReleaseEditor>(Configure);
                root.AddRoute<Shell>(route =>
                {
                    Configure(route);
                    route.AddChildRegion(Left, RegionCompositionMode.Layered, RegionOccupancy.Required, child =>
                    {
                        child.AddRoute<Hud>(hud =>
                        {
                            Configure(hud);
                            hud.AddChildRegion(Details, RegionCompositionMode.Layered, RegionOccupancy.Optional, details => details.AddRoute<Hud>(Configure));
                        });
                        child.AddRoute<Popup>(Configure);
                        child.AddRoute<Editor>(Configure);
                        child.AddRoute<ReleaseEditor>(Configure);
                    });
                    route.AddChildRegion(Right, RegionCompositionMode.Layered, RegionOccupancy.Required, child =>
                    {
                        child.AddRoute<Hud>(Configure);
                        child.AddRoute<Popup>(Configure);
                        child.AddRoute<Editor>(Configure);
                        child.AddRoute<ReleaseEditor>(Configure);
                    });
                });
            });
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
            {
                foreach (RegionDefinitionId region in new[] { Root, Left, Right, Details })
                {
                    builder.RegisterScreens(region, screens =>
                    {
                        screens.RegisterScreen(Define<Hud>());
                        if (region != Details)
                        {
                            screens.RegisterScreen(Define<Popup>());
                            screens.RegisterScreen(Define<Editor>());
                            screens.RegisterScreen(Define<ReleaseEditor>());
                        }
                        if (region == Root)
                        {
                            screens.RegisterScreen(Define<Shell>());
                        }
                        if (independentBlockers && (region == Left || region == Right))
                        {
                            screens.RegisterBlocker<Popup>(DefineBlocker());
                        }
                        else if (sharedRegistration && (region == Left || region == Right))
                        {
                            screens.RegisterBlocker<Popup>(common);
                        }
                    });
                }
            });
            Host = NavigationHost.Create(catalog, new NavigationHostOptions
            {
                DefaultBlocker = sharedRegistration ? null : common
            });
        }

        private BlockerDefinition DefineBlocker () => new((preparation, _) =>
        {
            Blocker blocker = preparation.Resources.CreateOwned(() => new Blocker(Endings));
            Blockers.Add(blocker);
            return new ValueTask<IBlockerPresenter>(blocker);
        });

        public Task StartSplitAsync (bool withDetails = false)
        {
            NavigationDestinationTree<Hud> left = Destination.For(new Hud("left"));
            if (withDetails)
            {
                left = left.Child(Details, Destination.For(new Hud("details")));
            }
            return Host.StartAsync(Destination.For(new Shell()).Child(Left, left).Child(Right, Destination.For(new Hud("right"))));
        }
        public RegionInstanceId Region (RegionDefinitionId definition) => Host.State.Current.Regions.Values.Single(region => region.DefinitionId == definition).Id;
        public Screen Current (RegionInstanceId region)
        {
            NavigationEntryId entry = Host.State.Current.GetRegion(region).Entries.Last();
            return Screens.Single(screen => screen.Entry == entry && screen.Disposals == 0);
        }
        private static void Configure<T> (RouteDefinitionBuilder<T> route) where T : Route
        {
            route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Reset | RouteEntryOperations.Replace;
            route.LowerPresentationPolicy = typeof(T) == typeof(Popup) ? LowerPresentationPolicy.BlockInput
                : typeof(T) == typeof(Editor) ? LowerPresentationPolicy.HideAndRetain
                : typeof(T) == typeof(ReleaseEditor) ? LowerPresentationPolicy.HideAndRelease
                : LowerPresentationPolicy.Preserve;
        }
        private ScreenDefinition<T> Define<T> () where T : Route
        {
            return new ScreenDefinition<T>((creation, _) =>
            {
                Screen screen = creation.Resources.CreateOwned(() => new Screen(this));
                Screens.Add(screen);
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.FrontView, screen.View }, screen));
                if (parentOwnedBlocker && typeof(T) == typeof(Shell))
                {
                    creation.RegisterScreens(Left, screens => screens.RegisterBlocker<Popup>(DefineBlocker()));
                }
                return new ValueTask<IScreenLifecycleHandler<T>>(screen);
            }, ScreenInstancePolicy.Multiple);
        }
    }

    private sealed class Screen : IScreenLifecycleHandler<Route>, IScreenStateCapture, IScreenAnimator, IDisposable
    {
        private readonly Fixture game;
        public Screen (Fixture game)
        {
            this.game = game;
            View = new View(() => game.FailValidation?.Invoke(this) == true);
        }
        public View View
        {
            get;
        }
        public View FrontView { get; } = new(order: 8);
        public NavigationEntryId Entry
        {
            get; private set;
        }
        public Route Route { get; private set; } = null!;
        public ScreenActivityContext Activity { get; private set; } = null!;
        public int Selection
        {
            get; set;
        }
        public int Preparations
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
        public List<ScreenAnimation> Animations { get; } = new();
        public object CaptureState () => Selection;
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (Route route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Entry = preparation.EntryId;
            Route = route;
            Preparations++;
            Selection = preparation.SavedState is int selection ? selection : 0;
            if (game.FailPrepare?.Invoke(this) == true)
            {
                throw new InvalidOperationException("Preparation failed.");
            }
            return default;
        }
        public ValueTask ActivateAsync (Route route, ScreenActivityContext activity)
        {
            Activity = activity;
            Activations++;
            return default;
        }
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
        public void Dispose ()
        {
            Disposals++;
            game.Endings.Add(this);
        }
        public void SetStateImmediately (ScreenAnimationState state)
        {
        }
        public async ValueTask PlayAsync (ScreenAnimation animation, CancellationToken cancellationToken)
        {
            Animations.Add(animation);
            if (game.Play is not null)
            {
                await game.Play(this, animation);
            }
        }
    }

    private sealed class Blocker : IBlockerPresenter, IDisposable
    {
        private readonly List<object> endings;
        public Blocker (List<object> endings) => this.endings = endings;
        public RegionInstanceId Region
        {
            get; private set;
        }
        public View View { get; } = new();
        public BlockerScreenContext? Context
        {
            get; private set;
        }
        public int Disposals
        {
            get; private set;
        }
        public ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken)
        {
            preparation.RegisterViewAdapter(View);
            return default;
        }
        public void SetScreenContext (BlockerScreenContext? context)
        {
            Context = context;
            if (context is not null)
            {
                Region = context.RegionId;
            }
        }
        public ValueTask TerminateAsync () => default;
        public void Dispose ()
        {
            Disposals++;
            endings.Add(this);
        }
    }

    private sealed class View : IViewAdapter
    {
        private readonly Func<bool>? failValidation;
        public View (Func<bool>? failValidation = null, int order = 0)
        {
            this.failValidation = failValidation;
            Presentation = new ViewPresentation(false, false, order);
        }
        public object Identity { get; } = new();
        public object OrderingDomain => "test";
        public bool IsAlive => true;
        public ViewPresentation Presentation
        {
            get; private set;
        }
        public event Action<string>? Lost { add { } remove { } }
        public void Validate (ViewPresentation presentation)
        {
            if (presentation.OutputEnabled && failValidation?.Invoke() == true)
            {
                throw new InvalidOperationException("View validation failed.");
            }
        }
        public void Apply (ViewPresentation presentation) => Presentation = presentation;
    }
}
