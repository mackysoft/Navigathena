using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.MicrosoftDI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenDependencyInjectionContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    public sealed record ItemRoute (int Id) : Route;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_rebuilds_screen_dependencies_without_rebuilding_or_disposing_shared_services (bool useDI)
    {
        SharedService shared = new();
        ScreenDefinition<ItemRoute> definition = new((creation, _) =>
        {
            if (useDI)
            {
                return new(creation.CreateScope(services =>
                {
                    services.AddSingleton(shared);
                    services.AddScoped<ScreenService>();
                    services.AddScreenLifecycleHandler<ItemPresenter>();
                }));
            }
            ScreenService service = creation.Lifetime.CreateOwned(() => new ScreenService(shared));
            return new(creation.Lifetime.CreateOwned(() => new ItemPresenter(shared, service)));
        });
        await using NavigationHost host = CreateHost(definition);
        await host.StartAsync(new ItemRoute(1));
        ItemPresenter previous = shared.Presenters[0];
        await previous.Activity!.Navigation.ReplaceAsync(new ItemRoute(2), new NavigationOptions { RecreateInstance = true });
        ItemPresenter current = shared.Presenters[1];
        Assert.NotSame(previous, current);
        Assert.NotSame(previous.Service, current.Service);
        Assert.Equal(new[] { "terminate", "presenter.dispose", "service.dispose" }, shared.Ending);
        Assert.Equal(0, shared.Disposals);
        Assert.Equal(new[] { 2 }, current.Inputs);
        await host.ShutdownAsync();
        Assert.Equal(2, shared.Ending.Count(item => item == "service.dispose"));
        Assert.Equal(0, shared.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_and_DI_construction_share_route_delivery_scope_lifetime_and_shutdown_order (bool useDI)
    {
        ServiceCollection application = new();
        application.AddSingleton<SharedService>();
        await using ServiceProvider parent = application.BuildServiceProvider();
        SharedService shared = parent.GetRequiredService<SharedService>();
        List<ItemPresenter> presenters = shared.Presenters;
        int scopes = 0;
        ScreenDefinition<ItemRoute> screen = new((creation, _) =>
        {
            scopes++;
            if (useDI)
            {
                return new(creation.CreateScope(services =>
                {
                    services.ImportService<SharedService>(parent);
                    services.AddScoped<ScreenService>();
                    services.AddScreenLifecycleHandler<ItemPresenter>();
                }));
            }
            else
            {
                ScreenService service = creation.Lifetime.CreateOwned(() => new ScreenService(shared));
                return new(creation.Lifetime.CreateOwned(() => new ItemPresenter(shared, service)));
            }
        });
        await using NavigationHost host = CreateHost(screen);
        Assert.Equal(NavigationResultKind.Committed, (await host.Start(new ItemRoute(1)).WaitAsync()).Kind);
        ItemPresenter presenter = Assert.Single(presenters);
        Assert.Equal(NavigationResultKind.Committed, (await presenter.Activity!.Navigation.Push(new ItemRoute(2)).WaitAsync()).Kind);
        Assert.Equal(NavigationResultKind.Committed, (await presenter.Activity!.Navigation.Back().WaitAsync()).Kind);
        Assert.Equal(new[] { 1, 2, 1 }, presenter.Inputs);
        Assert.Equal(1, scopes);
        Assert.Equal(1, presenter.InitializeCount);
        await host.ShutdownAsync();
        Assert.Equal(new[] { "terminate", "presenter.dispose", "service.dispose" }, shared.Ending);
        Assert.Equal(0, shared.Disposals);
    }

    [Fact]
    public async Task Multiple_instances_receive_separate_scoped_services_and_do_not_dispose_the_parent ()
    {
        SharedService shared = new();
        ScreenDefinition<ItemRoute> screen = new((creation, _) =>
        {
            return new(creation.CreateScope(services =>
            {
                services.AddSingleton(shared);
                services.AddScoped<ScreenService>();
                services.AddScreenLifecycleHandler<ItemPresenter>();
            }));
        }, ScreenInstancePolicy.Multiple);
        await using NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ItemRoute(1));
        await shared.Presenters[0].Activity!.Navigation.PushAsync(new ItemRoute(2));
        Assert.Equal(2, shared.Presenters.Count);
        Assert.NotSame(shared.Presenters[0].Service, shared.Presenters[1].Service);
        await host.ShutdownAsync();
        Assert.Equal(2, shared.Ending.Count(item => item == "service.dispose"));
        Assert.Equal(0, shared.Disposals);
    }

    [Fact]
    public async Task Scope_disposal_waits_for_async_services_and_releases_acquired_dependencies_afterwards ()
    {
        TaskCompletionSource<bool> disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool released = false;
        ScreenDefinition<ItemRoute> screen = new(async (creation, token) =>
        {
            await creation.Lifetime.AcquireAsync(new Acquisition(() => released = true), token);
            return creation.CreateScope(services =>
            {
                services.AddScoped(_ => new AsyncHandler(disposing, finish.Task));
                services.AddScreenLifecycleHandler<AsyncHandler>();
            });
        });
        NavigationHost host = CreateHost(screen);
        await host.StartAsync(new ItemRoute(1));
        Task shutdown = host.ShutdownAsync().AsTask();
        await disposing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(shutdown.IsCompleted);
        Assert.False(released);
        finish.SetResult(true);
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(released);
    }

    [Fact]
    public async Task Initialization_failure_stops_the_handler_before_disposing_the_private_scope ()
    {
        SharedService shared = new()
        {
            FailInitialization = true
        };
        ScreenDefinition<ItemRoute> screen = new((creation, _) =>
        {
            return new(creation.CreateScope(services =>
            {
                services.AddSingleton(shared);
                services.AddScoped<ScreenService>();
                services.AddScreenLifecycleHandler<ItemPresenter>();
            }));
        });
        await using NavigationHost host = CreateHost(screen);
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.StartAsync(new ItemRoute(1)));
        Assert.False(result.DestinationCommitted);
        Assert.Empty(host.State.Current.Entries);
        Assert.Equal(new[] { "terminate", "presenter.dispose", "service.dispose" }, shared.Ending);
        Assert.Equal(0, shared.Disposals);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-route")]
    public async Task Invalid_entry_point_configuration_fails_before_service_construction_and_releases_acquisitions (string configuration)
    {
        bool released = false;
        SharedService shared = new();
        ScreenDefinition<ItemRoute> screen = new(async (creation, token) =>
        {
            await creation.Lifetime.AcquireAsync(new Acquisition(() => released = true), token);
            return creation.CreateScope(services =>
            {
                services.AddSingleton(shared);
                services.AddScoped<ScreenService>();
                services.AddScoped<ItemPresenter>();
                if (configuration == "duplicate")
                {
                    services.AddScreenLifecycleHandler<ItemPresenter>();
                    services.AddScreenLifecycleHandler<OtherPresenter>();
                }
                else if (configuration == "wrong-route")
                {
                    services.AddScreenLifecycleHandler<OtherPresenter>();
                }
            });
        });
        await using NavigationHost host = CreateHost(screen);

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(async () => await host.StartAsync(new ItemRoute(1)));
        Assert.IsType<NavigationConfigurationException>(failure.InnerException);
        Assert.False(failure.DestinationCommitted);

        Assert.True(released);
        Assert.Empty(host.State.Current.Entries);
        Assert.Empty(shared.Presenters);
        Assert.Empty(shared.Ending);
    }

    private sealed record OtherRoute : Route;

    private sealed class OtherPresenter : IScreenLifecycleHandler<OtherRoute>
    {
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (OtherRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
        public ValueTask ActivateAsync (OtherRoute route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
    }

    private static NavigationHost CreateHost (ScreenDefinition<ItemRoute> screen)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<ItemRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
        }));
        return NavigationHost.Create(ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(Root, screens => screens.RegisterScreen(screen))));
    }

    public sealed class SharedService : IDisposable
    {
        public List<ItemPresenter> Presenters { get; } = new();
        public List<string> Ending { get; } = new();
        public bool FailInitialization
        {
            get; init;
        }
        public int Disposals
        {
            get; private set;
        }
        public void Dispose () => Disposals++;
    }

    public sealed class ScreenService : IDisposable
    {
        private readonly SharedService shared;
        public ScreenService (SharedService shared) => this.shared = shared;
        public void Dispose () => shared.Ending.Add("service.dispose");
    }

    public sealed class ItemPresenter : IScreenLifecycleHandler<ItemRoute>, IAsyncDisposable
    {
        private readonly SharedService shared;
        public ItemPresenter (SharedService shared, ScreenService service)
        {
            this.shared = shared;
            Service = service;
            shared.Presenters.Add(this);
        }
        public ScreenService Service
        {
            get;
        }
        public int InitializeCount
        {
            get; private set;
        }
        public List<int> Inputs { get; } = new();
        public ScreenActivityContext? Activity
        {
            get; private set;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken)
        {
            InitializeCount++;
            if (shared.FailInitialization)
            {
                throw new InvalidOperationException("Initialization failed.");
            }
            return default;
        }
        public ValueTask PrepareAsync (ItemRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Inputs.Add(route.Id);
            return default;
        }
        public ValueTask ActivateAsync (ItemRoute route, ScreenActivityContext activity)
        {
            Activity = activity;
            return default;
        }
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync ()
        {
            shared.Ending.Add("terminate");
            return default;
        }
        public ValueTask DisposeAsync ()
        {
            shared.Ending.Add("presenter.dispose");
            return default;
        }
    }

    private sealed class Acquisition : IResourceAcquisition<object>
    {
        private readonly Action release;
        public Acquisition (Action release) => this.release = release;
        public ValueTask<object> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken) => new(new object());
        public ValueTask DisposeAsync ()
        {
            release();
            return default;
        }
    }

    private sealed class AsyncHandler : IScreenLifecycleHandler<ItemRoute>, IAsyncDisposable
    {
        private readonly TaskCompletionSource<bool> disposing;
        private readonly Task finish;
        public AsyncHandler (TaskCompletionSource<bool> disposing, Task finish)
        {
            this.disposing = disposing;
            this.finish = finish;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (ItemRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
        public ValueTask ActivateAsync (ItemRoute route, ScreenActivityContext activity) => default;
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
        public async ValueTask DisposeAsync ()
        {
            disposing.SetResult(true);
            await finish;
        }
    }
}
