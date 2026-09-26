using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenCancellationContractTests
{
    private static readonly RegionDefinitionId Root = new("game");
    private static readonly string[] PreparationStages =
    {
        "source.deactivate", "effect.factory", "effect.begin", "screen.factory",
        "screen.initialize", "screen.prepare", "effect.switch", "blocker.factory", "blocker.prepare"
    };

    [Theory]
    [InlineData("source.deactivate")]
    [InlineData("effect.factory")]
    [InlineData("effect.begin")]
    [InlineData("screen.factory")]
    [InlineData("screen.initialize")]
    [InlineData("screen.prepare")]
    [InlineData("effect.switch")]
    [InlineData("blocker.factory")]
    [InlineData("blocker.prepare")]
    public async Task Cancellation_stops_subsequent_callbacks_even_when_the_current_callback_ignores_its_token (string stage)
    {
        List<string> events = new();
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask Visit (string name)
        {
            events.Add(name);
            if (name == stage)
            {
                entered.TrySetResult(true);
                await release.Task;
            }
        }

        Handler source = new("source", Visit);
        Handler screen = new("screen", Visit);
        Effect effect = new(Visit);
        Blocker blocker = new(Visit);
        await using NavigationHost host = Create(source, async (creation, _) =>
        {
            creation.Lifetime.CreateOwned(() => screen);
            await Visit("screen.factory");
            return screen;
        }, async (preparation, _) =>
        {
            preparation.Lifetime.CreateOwned(() => blocker);
            await Visit("blocker.factory");
            return blocker;
        });
        await host.StartAsync(new HomeRoute());
        events.Clear();
        NavigationTransition transition = new(NavigationTransitionScope.Region, async (preparation, _) =>
        {
            preparation.Lifetime.CreateOwned(() => effect);
            await Visit("effect.factory");
            return effect;
        });
        NavigationOperation operation = host.Client.Push(host.Root, Destination.For(new DialogRoute()), new NavigationOptions
        {
            Transition = transition
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(operation.TryRequestCancellation());
            Assert.False(operation.WaitAsync().IsCompleted);
            Assert.DoesNotContain("effect.dispose", events);
            Assert.DoesNotContain("screen.dispose", events);
            Assert.DoesNotContain("blocker.dispose", events);
        }
        finally
        {
            release.TrySetResult(true);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        foreach (string next in PreparationStages.Skip(Array.IndexOf(PreparationStages, stage) + 1))
        {
            Assert.DoesNotContain(next, events);
        }
        Assert.DoesNotContain("screen.activate", events);
        Assert.DoesNotContain("effect.after", events);
        Assert.IsType<HomeRoute>(host.State.Current.GetEntry(Assert.Single(host.State.Current.GetRegion(host.Root).Entries)).Route);
        Assert.Contains("source.activate", events);
        await host.ShutdownAsync();
        foreach (string participant in new[] { "effect", "screen", "blocker" })
        {
            if (events.Contains(participant + ".factory"))
            {
                Assert.Equal(1, events.Count(item => item == participant + ".dispose"));
                Assert.Contains(participant == "effect" ? "effect.settle" : participant + ".terminate", events);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acquisition_checks_cancellation_without_requiring_the_acquirer_to_check_and_still_disposes_it (bool cancelDuringAcquisition)
    {
        using CancellationTokenSource cancellation = new();
        Acquisition acquisition = new(() => cancellation.Cancel());
        Handler source = new("source", _ => default);
        await using NavigationHost host = Create(source, async (creation, _) =>
        {
            if (!cancelDuringAcquisition)
            {
                cancellation.Cancel();
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creation.Lifetime.AcquireAsync(acquisition, cancellation.Token).AsTask());
            Assert.Equal(0, acquisition.Disposals);
            return creation.Lifetime.CreateOwned(() => new Handler("screen", _ => default));
        });
        await host.StartAsync(new HomeRoute());
        await host.Client.PushAsync(host.Root, Destination.For(new DialogRoute()));
        Assert.Equal(cancelDuringAcquisition ? 1 : 0, acquisition.Calls);
        await host.ShutdownAsync();
        Assert.Equal(1, acquisition.Disposals);
    }

    [Fact]
    public async Task Shutdown_during_committed_effect_waits_for_work_and_settlement_without_rolling_back_history ()
    {
        List<string> events = new();
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask Visit (string name)
        {
            events.Add(name);
            if (name == "effect.after")
            {
                entered.TrySetResult(true);
                await release.Task;
            }
        }

        Handler source = new("source", Visit);
        NavigationHost host = Create(source, (creation, _) => new(creation.Lifetime.CreateOwned(() => new Handler("screen", Visit))));
        await host.StartAsync(new HomeRoute());
        NavigationTransition transition = new(NavigationTransitionScope.Region, (preparation, _) => new(preparation.Lifetime.CreateOwned(() => new Effect(Visit))));
        NavigationOperation operation = host.Client.Push(host.Root, Destination.For(new DialogRoute()), new NavigationOptions
        {
            Transition = transition
        });
        Task shutdown;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(operation.TryRequestCancellation());
            shutdown = host.ShutdownAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            Assert.DoesNotContain("effect.settle", events);
            Assert.DoesNotContain("screen.dispose", events);
        }
        finally
        {
            release.TrySetResult(true);
        }

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => operation.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(failure.DestinationCommitted);
        Assert.Equal(NavigationPresentationStatus.RecoveryRequired, failure.PresentationStatus);
        Assert.IsType<DialogRoute>(failure.FinalSnapshot.GetEntry(failure.FinalSnapshot.GetRegion(host.Root).Entries.Last()).Route);
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("screen.activate", events);
        Assert.Contains("effect.settle", events);
        Assert.Contains("screen.terminate", events);
        Assert.Equal(1, events.Count(item => item == "effect.dispose"));
        Assert.Equal(1, events.Count(item => item == "screen.dispose"));
    }

    private static NavigationHost Create (Handler source, Func<ScreenCreationContext<DialogRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<DialogRoute>>> create, BlockerFactory? blocker = null)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<HomeRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            });
            root.AddRoute<DialogRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
            });
        });
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, screens =>
        {
            screens.RegisterScreen(new ScreenDefinition<HomeRoute>((creation, _) => new(creation.Lifetime.CreateOwned(() => source))));
            screens.RegisterScreen(new ScreenDefinition<DialogRoute>(create));
        }));
        return NavigationHost.Create(catalog, new NavigationHostOptions
        {
            DefaultBlocker = blocker is null ? null : new BlockerDefinition(blocker)
        });
    }

    private sealed class Handler : IScreenLifecycleHandler<Route>, IAsyncDisposable
    {
        private readonly string name;
        private readonly Func<string, ValueTask> visit;
        public Handler (string name, Func<string, ValueTask> visit)
        {
            this.name = name;
            this.visit = visit;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => visit(name + ".initialize");
        public ValueTask PrepareAsync (Route route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => visit(name + ".prepare");
        public ValueTask ActivateAsync (Route route, ScreenActivityContext activity) => visit(name + ".activate");
        public ValueTask DeactivateAsync () => visit(name + ".deactivate");
        public ValueTask TerminateAsync () => visit(name + ".terminate");
        public ValueTask DisposeAsync () => visit(name + ".dispose");
    }

    private sealed class Effect : INavigationTransitionEffect, IAsyncDisposable
    {
        private readonly Func<string, ValueTask> visit;
        public Effect (Func<string, ValueTask> visit) => this.visit = visit;
        public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken) => visit("effect.begin");
        public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => visit("effect.switch");
        public ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => visit("effect.after");
        public ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken)
        {
            Assert.False(cancellationToken.CanBeCanceled);
            return visit("effect.settle");
        }
        public ValueTask DisposeAsync () => visit("effect.dispose");
    }

    private sealed class Blocker : IBlockerPresenter, IAsyncDisposable
    {
        private readonly Func<string, ValueTask> visit;
        public Blocker (Func<string, ValueTask> visit) => this.visit = visit;
        public ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken) => visit("blocker.prepare");
        public ValueTask TerminateAsync () => visit("blocker.terminate");
        public void SetScreenContext (BlockerScreenContext? context)
        {
        }
        public ValueTask DisposeAsync () => visit("blocker.dispose");
    }

    private sealed class Acquisition : IResourceAcquisition<object>
    {
        private readonly Action cancel;
        public Acquisition (Action cancel) => this.cancel = cancel;
        public int Calls
        {
            get; private set;
        }
        public int Disposals
        {
            get; private set;
        }
        public ValueTask<object> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            cancel();
            return new(new object());
        }
        public ValueTask DisposeAsync ()
        {
            Disposals++;
            return default;
        }
    }

    private sealed record HomeRoute : Route;
    private sealed record DialogRoute : Route;
}
