using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Awaits region navigation through the client's fundamental operations.</summary>
    /// <remarks>Async methods request cancellation of the operation and wait for it to settle. Rejected requests and execution failures throw.</remarks>
    public static class NavigationClientExtensions
    {
        public static NavigationOperation Replace<TRoute> (this INavigationClient navigation, RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route
            => (navigation ?? throw new ArgumentNullException(nameof(navigation))).ReplaceFrom(target, HistoryTarget.Current, destination, options);

        public static Task ReplaceFromAsync<TTarget> (this INavigationClient navigation, RegionInstanceId region, Route route, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TTarget : NavigationRoute
            => navigation.ReplaceFromAsync(region, HistoryTarget.Unique<TTarget>(), Destination.For(route), options, cancellationToken);

        public static Task ReplaceFromAsync<TRoute> (this INavigationClient navigation, RegionInstanceId region, HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).ReplaceFrom(region, target, destination, options), cancellationToken);

        public static Task PushAsync<TRoute> (this INavigationClient navigation, RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Push(target, destination, options), cancellationToken);

        public static Task ReplaceAsync<TRoute> (this INavigationClient navigation, RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Replace(target, destination, options), cancellationToken);

        public static Task ResetAsync<TRoute> (this INavigationClient navigation, RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Reset(target, destination, options), cancellationToken);

        public static Task BackAsync (this INavigationClient navigation, RegionInstanceId target, BackOptions? options = null, CancellationToken cancellationToken = default)
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Back(target, options), cancellationToken);

        public static Task ReloadAsync (this INavigationClient navigation, RegionInstanceId target, ReloadOptions? options = null, CancellationToken cancellationToken = default)
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Reload(target, options), cancellationToken);

        public static Task ClearAsync (this INavigationClient navigation, RegionInstanceId target, CancellationToken cancellationToken = default)
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Clear(target), cancellationToken);
    }
}
