using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.MicrosoftDI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationFailureContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private sealed record TestRoute : Route;

    [Theory]
    [InlineData("construction", false)]
    [InlineData("initialize", false)]
    [InlineData("prepare", false)]
    [InlineData("activate", false)]
    [InlineData("construction", true)]
    [InlineData("initialize", true)]
    [InlineData("prepare", true)]
    [InlineData("activate", true)]
    public async Task Execution_failure_interrupts_the_caller_and_preserves_the_original_exception_and_commit (string phase, bool useDI)
    {
        Exception original = new InvalidOperationException("Game callback failed.");
        Handler handler = new()
        {
            Phase = phase,
            Failure = original
        };
        await using NavigationHost host = Create((creation, _) =>
        {
            if (useDI)
            {
                return new(creation.CreateScope(services =>
                {
                    services.AddScoped(_ => phase == "construction" ? throw original : handler);
                    services.AddScreenLifecycleHandler<Handler>();
                }));
            }
            else
            {
                Handler owned = creation.Resources.CreateOwned(() => phase == "construction" ? throw original : handler);
                return new(owned);
            }
        });
        bool nextStepRan = false;

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(async () =>
        {
            await host.StartAsync(new TestRoute());
            nextStepRan = true;
        });

        Assert.False(nextStepRan);
        Assert.Contains(original, Causes(failure));
        Assert.Equal(phase == "activate", failure.DestinationCommitted);
        Assert.Equal(phase == "activate" ? 1 : 0, host.State.Current.GetRegion(host.Root).Entries.Count);
        Assert.Same(host.State.Current, failure.FinalSnapshot);
        if (phase == "activate")
        {
            Assert.Equal(NavigationPresentationStatus.RecoveryRequired, failure.PresentationStatus);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_and_dispose_join_one_failed_attempt_without_running_dependent_disposal (bool useDI)
    {
        Exception original = new InvalidOperationException("Cannot release handler.");
        TaskCompletionSource<bool> disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Handler handler = new()
        {
            Phase = "dispose",
            Failure = original,
            Disposing = disposing,
            Release = release.Task
        };
        Dependency dependency = new();
        NavigationHost host = Create((creation, _) =>
        {
            creation.Resources.CreateOwned(() => dependency);
            if (useDI)
            {
                return new(creation.CreateScope(services =>
                {
                    services.AddScoped(_ => handler);
                    services.AddScreenLifecycleHandler<Handler>();
                }));
            }
            else
            {
                return new(creation.Resources.CreateOwned(() => handler));
            }
        });
        await host.StartAsync(new TestRoute());
        bool commonServicesDisposed = false;
        Task shutdown = EndApplicationAsync();
        await disposing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = ((IAsyncDisposable)host).DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        Assert.False(disposal.IsCompleted);
        release.SetResult(true);

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => shutdown);
        AggregateException joined = await Assert.ThrowsAsync<AggregateException>(() => disposal);
        AggregateException repeated = await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());

        Assert.Same(failure, joined);
        Assert.Same(failure, repeated);
        Assert.Contains(original, failure.Flatten().InnerExceptions);
        Assert.False(commonServicesDisposed);
        Assert.False(dependency.Disposed);
        Assert.Equal(1, handler.Disposals);
        Assert.Contains(original, Causes(Assert.Single(host.Terminations.Current.Records).Exception!));

        async Task EndApplicationAsync ()
        {
            await ((INavigationHost)host).ShutdownAsync();
            commonServicesDisposed = true;
        }
    }

    [Fact]
    public async Task Shutdown_collects_independent_failures_without_releasing_their_dependencies ()
    {
        Handler first = new()
        {
            Phase = "terminate",
            Failure = new InvalidOperationException("First stop failed.")
        };
        Handler second = new()
        {
            Phase = "terminate",
            Failure = new InvalidOperationException("Second stop failed.")
        };
        int count = 0;
        NavigationHost host = Create((creation, _) =>
        {
            return new(creation.Resources.CreateOwned(() => count++ == 0 ? first : second));
        }, ScreenInstancePolicy.Multiple);
        await host.StartAsync(new TestRoute());
        await host.Client.PushAsync(host.Root, Destination.For(new TestRoute()));

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());

        Assert.Contains(first.Failure, failure.Flatten().InnerExceptions);
        Assert.Contains(second.Failure, failure.Flatten().InnerExceptions);
        Assert.Equal(0, first.Disposals);
        Assert.Equal(0, second.Disposals);
    }

    [Fact]
    public async Task Cancellation_callback_failure_is_thrown_and_does_not_repeat_the_cancellation ()
    {
        Exception original = new InvalidOperationException("Cancellation callback failed.");
        TaskCompletionSource<bool> preparing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbacks = 0;
        await using NavigationHost host = Create(async (_, token) =>
        {
            using CancellationTokenRegistration registration = token.Register(() =>
            {
                callbacks++;
                throw original;
            });
            preparing.SetResult(true);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("The construction must be cancelled.");
        });
        NavigationOperation operation = host.Start(new TestRoute());
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5));

        AggregateException failure = Assert.Throws<AggregateException>(() => operation.TryRequestCancellation());

        Assert.Contains(original, failure.Flatten().InnerExceptions);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync());
        Assert.False(operation.TryRequestCancellation());
        Assert.Equal(1, callbacks);
        Assert.Empty(host.State.Current.Entries);
    }

    [Fact]
    public async Task Completion_observer_failure_keeps_the_committed_screen_and_original_cause ()
    {
        Exception original = new InvalidOperationException("Observer failed.");
        await using NavigationHost host = Create((_, _) => new(new Handler()), options: new NavigationHostOptions { OperationCompleted = _ => throw original });

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(async () => await host.StartAsync(new TestRoute()));

        Assert.Same(original, failure.InnerException);
        Assert.True(failure.DestinationCommitted);
        Assert.Single(host.State.Current.Entries);
    }

    private static NavigationHost Create (Func<ScreenCreationContext<TestRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TestRoute>>> create, ScreenInstancePolicy policy = ScreenInstancePolicy.Single, NavigationHostOptions? options = null)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<TestRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
        }));
        ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, screens => screens.RegisterScreen(new ScreenDefinition<TestRoute>(create, policy))));
        return NavigationHost.Create(catalog, options);
    }

    private static Exception[] Causes (Exception exception) => new[] { exception }.Concat(exception is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Causes)
        : exception.InnerException is Exception inner ? Causes(inner) : Array.Empty<Exception>()).ToArray();

    private sealed class Handler : IScreenLifecycleHandler<TestRoute>, IAsyncDisposable
    {
        public string? Phase
        {
            get; init;
        }
        public Exception Failure { get; init; } = new InvalidOperationException();
        public TaskCompletionSource<bool>? Disposing
        {
            get; init;
        }
        public Task Release { get; init; } = Task.CompletedTask;
        public int Disposals
        {
            get; private set;
        }

        public ValueTask InitializeAsync (CancellationToken cancellationToken) => Fail("initialize");
        public ValueTask PrepareAsync (TestRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => Fail("prepare");
        public ValueTask ActivateAsync (TestRoute route, ScreenActivityContext activity) => Fail("activate");
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => Fail("terminate");

        public async ValueTask DisposeAsync ()
        {
            Disposals++;
            Disposing?.TrySetResult(true);
            await Release;
            await Fail("dispose");
        }

        private ValueTask Fail (string phase)
        {
            if (Phase == phase)
            {
                throw Failure;
            }
            return default;
        }
    }

    private sealed class Dependency : IAsyncDisposable
    {
        public bool Disposed
        {
            get; private set;
        }
        public ValueTask DisposeAsync ()
        {
            Disposed = true;
            return default;
        }
    }
}
