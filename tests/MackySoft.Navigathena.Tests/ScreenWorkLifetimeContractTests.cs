using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenWorkLifetimeContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finished_work_releases_its_callback_while_its_screen_and_result_remain_reachable (bool cancelPending)
    {
        WorkScreen screen = new();
        ScreenWork? work = null;
        WeakReference? payload = null;
        if (cancelPending)
        {
            screen.Activating = activity =>
            {
                (work, payload) = StartCapturedWork(activity);
                work.Cancel();
                return default;
            };
        }
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new PageRoute());
        if (!cancelPending)
        {
            (work, payload) = StartCapturedWork(screen.Activity!);
        }
        Assert.NotNull(work);
        Assert.NotNull(payload);
        if (cancelPending)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync().WaitAsync(TestTimeout));
        }
        else
        {
            await work.WaitAsync().WaitAsync(TestTimeout);
        }

        await CollectAsync(payload);

        Assert.False(payload.IsAlive);
        Assert.Equal(0, screen.Disposals);
        work.Cancel();
        GC.KeepAlive(work);
        GC.KeepAlive(host);
    }

    [Fact]
    public async Task A_retained_completed_result_does_not_retain_its_ended_screen ()
    {
        (ScreenWork work, WeakReference screen) = await CompleteAndEndScreenAsync();

        await CollectAsync(screen);

        Assert.False(screen.IsAlive);
        await work.WaitAsync().WaitAsync(TestTimeout);
        work.Cancel();
        GC.KeepAlive(work);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Work_failure_is_observable_without_preventing_final_termination_and_release (bool failDuringShutdown)
    {
        WorkScreen screen = new();
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new PageRoute());
        TaskCompletionSource<bool> entered = Signal();
        InvalidOperationException failure = new("The screen work failed.");
        ScreenWork work = screen.Activity!.StartWork(async context =>
        {
            entered.SetResult(true);
            try
            {
                if (failDuringShutdown)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }
            }
            finally
            {
                screen.Preparation!.Use();
                throw failure;
            }
        });
        await entered.Task.WaitAsync(TestTimeout);
        Task shutdown = failDuringShutdown ? host.ShutdownAsync().AsTask() : Task.CompletedTask;

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => work.WaitAsync().WaitAsync(TestTimeout)));
        await shutdown.WaitAsync(TestTimeout);
        await host.ShutdownAsync().AsTask().WaitAsync(TestTimeout);

        Assert.Equal(1, screen.Terminations);
        Assert.Equal(1, screen.Disposals);
        Assert.Equal(1, screen.Preparation!.Disposals);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => work.WaitAsync()));
    }

    [Fact]
    public async Task Cancel_waits_for_async_finally_and_keeps_screen_resources_available ()
    {
        WorkScreen screen = new();
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new PageRoute());
        TaskCompletionSource<bool> entered = Signal();
        TaskCompletionSource<bool> finishing = Signal();
        TaskCompletionSource<bool> release = Signal();
        ScreenWork work = screen.Activity!.StartWork(async context =>
        {
            entered.SetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            finally
            {
                finishing.SetResult(true);
                await release.Task;
                screen.Preparation!.Use();
            }
        });
        await entered.Task.WaitAsync(TestTimeout);
        work.Cancel();
        await finishing.Task.WaitAsync(TestTimeout);
        Task shutdown = host.ShutdownAsync().AsTask();
        try
        {
            Assert.False(work.WaitAsync().IsCompleted);
            Assert.False(shutdown.IsCompleted);
            Assert.Equal(0, screen.Disposals);
            Assert.Equal(0, screen.Preparation!.Disposals);
        }
        finally
        {
            release.TrySetResult(true);
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync().WaitAsync(TestTimeout));
        await shutdown.WaitAsync(TestTimeout);
        Assert.Equal(1, screen.Disposals);
        Assert.Equal(1, screen.Preparation!.Disposals);
    }

    [Fact]
    public async Task Shutdown_stops_activity_while_waiting_for_work_to_finish ()
    {
        WorkScreen screen = new();
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new PageRoute());
        TaskCompletionSource<bool> entered = Signal();
        TaskCompletionSource<bool> deactivated = Signal();
        screen.Deactivating = () => deactivated.TrySetResult(true);
        ScreenWork work = screen.Activity!.StartWork(async _ =>
        {
            entered.SetResult(true);
            await deactivated.Task;
            screen.Preparation!.Use();
        });
        await entered.Task.WaitAsync(TestTimeout);
        Task shutdown = host.ShutdownAsync().AsTask();
        try
        {
            await shutdown.WaitAsync(TestTimeout);
            Assert.True(deactivated.Task.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync());
            Assert.Equal(1, screen.Terminations);
        }
        finally
        {
            deactivated.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Failed_reactivation_discards_its_work_even_when_older_work_finishes_during_activation ()
    {
        WorkScreen screen = new();
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new PageRoute());
        TaskCompletionSource<bool> releaseOld = Signal();
        TaskCompletionSource<bool> oldStarted = Signal();
        ScreenWork old = screen.Activity!.StartWork(async _ =>
        {
            oldStarted.SetResult(true);
            await releaseOld.Task;
        });
        await oldStarted.Task.WaitAsync(TestTimeout);
        await host.Client.Push(host.Root, Destination.For(new PageRoute())).WaitAsync();
        TaskCompletionSource<bool> activating = Signal();
        TaskCompletionSource<bool> fail = Signal();
        ScreenWork? pending = null;
        bool ran = false;
        screen.Activating = async activity =>
        {
            pending = activity.StartWork(_ =>
            {
                ran = true;
                return default;
            });
            activating.SetResult(true);
            await fail.Task;
            throw new InvalidOperationException("Reactivation failed.");
        };
        Task back = host.Client.BackAsync(host.Root);
        try
        {
            await activating.Task.WaitAsync(TestTimeout);
            releaseOld.SetResult(true);
            await old.WaitAsync().WaitAsync(TestTimeout);
        }
        finally
        {
            releaseOld.TrySetResult(true);
            fail.TrySetResult(true);
        }
        await Assert.ThrowsAsync<NavigationException>(() => back.WaitAsync(TestTimeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending!.WaitAsync().WaitAsync(TestTimeout));
        Assert.False(ran);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ScreenWork Work, WeakReference Payload) StartCapturedWork (ScreenActivityContext activity)
    {
        Payload payload = new();
        return (activity.StartWork(payload.RunAsync), new WeakReference(payload));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(ScreenWork Work, WeakReference Screen)> CompleteAndEndScreenAsync ()
    {
        WorkScreen screen = new();
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new PageRoute());
        ScreenWork work = screen.Activity!.StartWork(_ => default);
        await work.WaitAsync().WaitAsync(TestTimeout);
        await host.ShutdownAsync();
        return (work, new WeakReference(screen));
    }

    private static async Task CollectAsync (WeakReference reference)
    {
        Stopwatch waiting = Stopwatch.StartNew();
        while (!IsCollected(reference) && waiting.Elapsed < TestTimeout)
        {
            // Completion can resume its observer before the producer's stack has returned.
            await Task.Delay(1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsCollected (WeakReference reference)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return !reference.IsAlive;
    }

    private static TaskCompletionSource<bool> Signal () => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static NavigationHost CreateHost (WorkScreen first)
    {
        int constructions = 0;
        ScreenDefinition<PageRoute> definition = new((creation, _) => new(creation.Lifetime.CreateOwned(() =>
            constructions++ == 0 ? first : new WorkScreen())), ScreenInstancePolicy.Multiple);
        return NavigationHost.Create(ScreenCatalog.Build(screens =>
            screens.Register(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.HideAndRetain, definition)));
    }

    private sealed record PageRoute : Route;

    private sealed class Payload
    {
        public ValueTask RunAsync (ScreenWorkContext context)
        {
            GC.KeepAlive(this);
            return default;
        }
    }

    private sealed class WorkScreen : IScreenLifecycleHandler<PageRoute>, IDisposable
    {
        public ScreenActivityContext? Activity { get; private set; }
        public Resource? Preparation { get; private set; }
        public Func<ScreenActivityContext, ValueTask>? Activating { get; set; }
        public Action? Deactivating { get; set; }
        public int Terminations { get; private set; }
        public int Disposals { get; private set; }
        public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (PageRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Preparation = preparation.Lifetime.CreateOwned(() => new Resource());
            return default;
        }
        public ValueTask ActivateAsync (PageRoute route, ScreenActivityContext activity)
        {
            Activity = activity;
            return Activating?.Invoke(activity) ?? default;
        }
        public ValueTask DeactivateAsync ()
        {
            Deactivating?.Invoke();
            return default;
        }
        public ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            Preparation!.Use();
            Terminations++;
            return default;
        }
        public void Dispose () => Disposals++;
    }

    private sealed class Resource : IDisposable
    {
        public int Disposals { get; private set; }
        public void Use () => ObjectDisposedException.ThrowIf(Disposals != 0, this);
        public void Dispose () => Disposals++;
    }
}
