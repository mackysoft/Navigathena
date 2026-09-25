using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationConvenienceContractTests
{
    [Theory]
    [InlineData(NavigationOperationKind.Push, false)]
    [InlineData(NavigationOperationKind.Push, true)]
    [InlineData(NavigationOperationKind.Replace, false)]
    [InlineData(NavigationOperationKind.Replace, true)]
    [InlineData(NavigationOperationKind.Reset, false)]
    [InlineData(NavigationOperationKind.Reset, true)]
    public async Task Consumer_navigation_decorators_handle_single_routes_and_destination_trees_through_the_same_operations (NavigationOperationKind kind, bool useTree)
    {
        Handler handler = new();
        await using NavigationHost host = CreateHost(handler);
        await host.StartAsync(new TestRoute(1));
        RecordingNavigation navigation = new(handler.Activity!.Navigation);
        TestRoute destination = new(2);
        NavigationOptions options = new()
        {
            Transition = NavigationTransition.None
        };

        switch (kind)
        {
            case NavigationOperationKind.Push:
                await (useTree
                    ? navigation.PushAsync(Destination.For(destination), options)
                    : navigation.PushAsync(destination, options));
                break;
            case NavigationOperationKind.Replace:
                await (useTree
                    ? navigation.ReplaceAsync(Destination.For(destination), options)
                    : navigation.ReplaceAsync(destination, options));
                break;
            case NavigationOperationKind.Reset:
                await (useTree
                    ? navigation.ResetAsync(Destination.For(destination), options)
                    : navigation.ResetAsync(destination, options));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Assert.Equal(1, navigation.RequestCount);
        Assert.Same(options, navigation.LastOptions);
        Assert.Equal(destination, handler.Route);
        Assert.Equal(kind == NavigationOperationKind.Push ? 2 : 1, host.State.Current.GetRegion(host.Root).Entries.Count);
    }

    [Fact]
    public async Task Already_cancelled_requests_do_not_reach_the_navigation_implementation ()
    {
        Handler handler = new();
        await using NavigationHost host = CreateHost(handler);
        await host.StartAsync(new TestRoute(1));
        RecordingNavigation navigation = new(handler.Activity!.Navigation);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation.PushAsync(new TestRoute(2), cancellationToken: cancellation.Token));

        Assert.Equal(0, navigation.RequestCount);
        Assert.Equal(new TestRoute(1), handler.Route);
        Assert.Single(host.State.Current.GetRegion(host.Root).Entries);
    }

    [Fact]
    public async Task Convenience_operations_do_not_bypass_expired_activity_validation ()
    {
        Handler handler = new();
        await using NavigationHost host = CreateHost(handler);
        await host.StartAsync(new TestRoute(1));
        IScreenNavigation oldActivity = handler.Activity!.Navigation;
        await oldActivity.PushAsync(new TestRoute(2));
        long revision = host.State.Current.Revision;

        await Assert.ThrowsAsync<NavigationException>(() => oldActivity.ResetAsync(new TestRoute(3)));

        Assert.Equal(revision, host.State.Current.Revision);
        Assert.Equal(new TestRoute(2), handler.Route);
    }

    private static NavigationHost CreateHost (Handler handler)
    {
        return NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register<TestRoute>((_, _) => new(handler), route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
        })));
    }

    private sealed record TestRoute (int Value) : Route;

    private sealed class Handler : IScreenLifecycleHandler<TestRoute>
    {
        public ScreenActivityContext? Activity
        {
            get; private set;
        }
        public TestRoute? Route
        {
            get; private set;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (TestRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
        public ValueTask ActivateAsync (TestRoute route, ScreenActivityContext activity)
        {
            Route = route;
            Activity = activity;
            return default;
        }
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => default;
    }

    private sealed class RecordingNavigation : IScreenNavigation
    {
        private readonly IScreenNavigation navigation;

        public RecordingNavigation (IScreenNavigation navigation) => this.navigation = navigation;
        public int RequestCount
        {
            get; private set;
        }
        public NavigationOptions? LastOptions
        {
            get; private set;
        }

        public NavigationOperation Push<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route
            => Record(() => navigation.Push(destination, options), options);
        public NavigationOperation ReplaceFrom<TRoute> (HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route
            => Record(() => navigation.ReplaceFrom(target, destination, options), options);
        public NavigationOperation Reset<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route
            => Record(() => navigation.Reset(destination, options), options);
        public NavigationOperation Back (BackOptions? options = null) => navigation.Back(options);
        public NavigationOperation Reload (ReloadOptions? options = null) => navigation.Reload(options);
        public Task InvokeAsync (Route route, CancellationToken cancellationToken = default, NavigationOptions? options = null) => navigation.InvokeAsync(route, cancellationToken, options);
        public Task<TResult> InvokeAsync<TResult> (Route<TResult> route, CancellationToken cancellationToken = default, NavigationOptions? options = null) => navigation.InvokeAsync(route, cancellationToken, options);
        public void PostPush<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => navigation.PostPush(route, options);
        public void PostReplace<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => navigation.PostReplace(route, options);
        public void PostReset<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => navigation.PostReset(route, options);
        public void PostBack (BackOptions? options = null) => navigation.PostBack(options);
        public IScreenNavigation GetRegionNavigation (RegionTarget target) => navigation.GetRegionNavigation(target);

        private NavigationOperation Record (Func<NavigationOperation> request, NavigationOptions? options)
        {
            RequestCount++;
            LastOptions = options;
            return request();
        }
    }
}
