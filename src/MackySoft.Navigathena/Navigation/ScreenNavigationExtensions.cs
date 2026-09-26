using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Converts single routes and awaits screen navigation through the same fundamental operations.</summary>
    /// <remarks>Async methods request cancellation of the operation and wait for it to settle. Rejected requests and execution failures throw.</remarks>
    public static class ScreenNavigationExtensions
    {
        public static NavigationOperation Push<TRoute> (this IScreenNavigation navigation, TRoute route, NavigationOptions? options = null) where TRoute : Route
            => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Push(Destination.For(route), options);

        public static Task PushAsync<TRoute> (this IScreenNavigation navigation, TRoute route, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Push(route, options), cancellationToken);

        public static Task PushAsync<TRoute> (this IScreenNavigation navigation, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Push(destination, options), cancellationToken);

        public static NavigationOperation Replace<TRoute> (this IScreenNavigation navigation, TRoute route, NavigationOptions? options = null) where TRoute : Route
            => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Replace(Destination.For(route), options);

        public static NavigationOperation Replace<TRoute> (this IScreenNavigation navigation, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route
            => (navigation ?? throw new ArgumentNullException(nameof(navigation))).ReplaceFrom(HistoryTarget.Current, destination, options);

        public static Task ReplaceFromAsync<TTarget> (this IScreenNavigation navigation, Route route, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TTarget : NavigationRoute
            => navigation.ReplaceFromAsync(HistoryTarget.Unique<TTarget>(), Destination.For(route), options, cancellationToken);

        public static Task ReplaceFromAsync<TRoute> (this IScreenNavigation navigation, HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).ReplaceFrom(target, destination, options), cancellationToken);

        public static Task ReplaceAsync<TRoute> (this IScreenNavigation navigation, TRoute route, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Replace(route, options), cancellationToken);

        public static Task ReplaceAsync<TRoute> (this IScreenNavigation navigation, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Replace(destination, options), cancellationToken);

        public static NavigationOperation Reset<TRoute> (this IScreenNavigation navigation, TRoute route, NavigationOptions? options = null) where TRoute : Route
            => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Reset(Destination.For(route), options);

        public static Task ResetAsync<TRoute> (this IScreenNavigation navigation, TRoute route, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Reset(route, options), cancellationToken);

        public static Task ResetAsync<TRoute> (this IScreenNavigation navigation, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null, CancellationToken cancellationToken = default) where TRoute : Route
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Reset(destination, options), cancellationToken);

        public static Task BackAsync (this IScreenNavigation navigation, BackOptions? options = null, CancellationToken cancellationToken = default)
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Back(options), cancellationToken);

        public static Task ReloadAsync (this IScreenNavigation navigation, ReloadOptions? options = null, CancellationToken cancellationToken = default)
            => NavigationOperation.ExecuteAsync(() => (navigation ?? throw new ArgumentNullException(nameof(navigation))).Reload(options), cancellationToken);

    }
}
