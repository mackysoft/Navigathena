using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenResourceLifetimeContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Owned_objects_and_acquisitions_release_in_reverse_registration_order_after_termination ()
    {
        List<string> released = new();
        await using NavigationHost host = CreateHost(async (creation, token) =>
        {
            Resource provider = creation.Lifetime.CreateOwned(() => new Resource("provider", released));
            Resource acquired = await creation.Lifetime.AcquireAsync(new Acquisition(new Resource("acquisition", released, provider.Use)), token);
            return creation.Lifetime.CreateOwned(() => new Handler(acquired, released));
        });
        await host.StartAsync(new PageRoute());

        await host.ShutdownAsync().AsTask().WaitAsync(TestTimeout);

        Assert.Equal(new[] { "terminate", "preparation", "initialization", "handler", "acquisition", "provider" }, released);
    }

    [Fact]
    public async Task Acquisition_release_order_depends_on_registration_not_completion_order ()
    {
        List<string> released = new();
        TaskCompletionSource<bool> firstCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Resource first = new("first", released);
        Resource second = new("second", released);
        await using NavigationHost host = CreateHost(async (creation, token) =>
        {
            Task<Resource> pending = creation.Lifetime.AcquireAsync(new Acquisition(first) { Completion = firstCompletion.Task }, token).AsTask();
            await creation.Lifetime.AcquireAsync(new Acquisition(second), token);
            secondCompleted.SetResult(true);
            await pending;
            return creation.Lifetime.CreateOwned(() => new Handler(first, released));
        });
        Task start = host.StartAsync(new PageRoute());
        try
        {
            await secondCompleted.Task.WaitAsync(TestTimeout);
            Assert.False(start.IsCompleted);
        }
        finally
        {
            firstCompletion.TrySetResult(true);
        }
        await start.WaitAsync(TestTimeout);

        await host.ShutdownAsync().AsTask().WaitAsync(TestTimeout);

        Assert.Equal(new[] { "second", "first" }, released.Where(item => item == "first" || item == "second"));
    }

    [Fact]
    public async Task Failed_release_retains_earlier_owned_and_borrowed_dependencies_without_retrying_partial_disposal ()
    {
        List<string> released = new();
        Resource external = new("external", released);
        ResourceLifetime lifetime = new();
        ResourceReference<Resource> reference = lifetime.Reference(external);
        Resource provider = new("provider", released, external.Use);
        Resource failing = new("failing", released, provider.Use) { FailDisposal = true };
        NavigationHost host = CreateHost(async (creation, token) =>
        {
            await creation.Lifetime.BorrowAsync(reference, token);
            creation.Lifetime.CreateOwned(() => provider);
            await creation.Lifetime.AcquireAsync(new Acquisition(failing), token);
            return creation.Lifetime.CreateOwned(() => new Handler(failing, released));
        });
        await host.StartAsync(new PageRoute());

        Task first = host.ShutdownAsync().AsTask();
        Task joined = host.ShutdownAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => first.WaitAsync(TestTimeout));
        await Assert.ThrowsAsync<AggregateException>(() => joined.WaitAsync(TestTimeout));
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask().WaitAsync(TestTimeout));
        await Assert.ThrowsAnyAsync<Exception>(() => lifetime.EndAsync().AsTask()).WaitAsync(TestTimeout);

        Assert.Equal(1, failing.Disposals);
        Assert.Equal(0, provider.Disposals);
        Assert.Equal(0, external.Disposals);
        provider.Use();
    }

    private static NavigationHost CreateHost (Func<ScreenCreationContext<PageRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<PageRoute>>> create)
        => NavigationHost.Create(ScreenCatalog.Build(screens =>
            screens.Register(RouteEntryOperations.Reset, LowerPresentationPolicy.HideAndRetain, create)));

    private sealed record PageRoute : Route;

    private sealed class Handler : IScreenLifecycleHandler<PageRoute>, IDisposable
    {
        private readonly Resource dependency;
        private readonly List<string> released;
        private bool disposed;
        private Resource? initialization;
        private Resource? preparation;

        public Handler (Resource dependency, List<string> released)
        {
            this.dependency = dependency;
            this.released = released;
        }

        private void Use ()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            dependency.Use();
        }

        public async ValueTask InitializeAsync (ScreenInitializationContext context, CancellationToken cancellationToken)
        {
            initialization = await context.Lifetime.AcquireAsync(new Acquisition(new Resource("initialization", released, Use)), cancellationToken);
        }

        public async ValueTask PrepareAsync (PageRoute route, ScreenPreparationContext context, CancellationToken cancellationToken)
        {
            preparation = await context.Lifetime.AcquireAsync(new Acquisition(new Resource("preparation", released, initialization!.Use)), cancellationToken);
        }

        public ValueTask ActivateAsync (PageRoute route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            preparation!.Use();
            initialization!.Use();
            Use();
            released.Add("terminate");
            return default;
        }

        public void Dispose ()
        {
            dependency.Use();
            disposed = true;
            released.Add("handler");
        }
    }

    private sealed class Resource : IDisposable
    {
        private readonly string name;
        private readonly List<string> released;
        private readonly Action? useDependency;

        public Resource (string name, List<string> released, Action? useDependency = null)
        {
            this.name = name;
            this.released = released;
            this.useDependency = useDependency;
        }

        public int Disposals { get; private set; }
        public bool FailDisposal { get; init; }

        public void Use ()
        {
            ObjectDisposedException.ThrowIf(Disposals != 0, this);
            useDependency?.Invoke();
        }

        public void Dispose ()
        {
            useDependency?.Invoke();
            Disposals++;
            released.Add(name);
            if (FailDisposal)
            {
                throw new InvalidOperationException("The resource was partially disposed.");
            }
        }
    }

    private sealed class Acquisition : IResourceAcquisition<Resource>
    {
        private readonly Resource resource;
        public Acquisition (Resource resource) => this.resource = resource;
        public Task Completion { get; init; } = Task.CompletedTask;
        public async ValueTask<Resource> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            await Completion.WaitAsync(cancellationToken);
            resource.Use();
            return resource;
        }
        public ValueTask ReleaseAsync (NavigationProgressReporter progress)
        {
            resource.Dispose();
            return default;
        }
    }
}
