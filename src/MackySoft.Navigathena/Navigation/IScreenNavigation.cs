using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Defines fundamental navigation operations from a live screen. Route conveniences and transition waiting are supplied by extension methods.</summary>
    public interface IScreenNavigation
    {
        /// <summary>Waits for a screen to close, its resources to finish and the caller to resume. Activity suspension does not cancel an accepted call.</summary>
        Task InvokeAsync (Route route, CancellationToken cancellationToken = default, NavigationOptions? options = null);
        /// <summary>Returns the answer after cleanup and caller resumption. Closing without an answer cancels the task.</summary>
        Task<TResult> InvokeAsync<TResult> (Route<TResult> route, CancellationToken cancellationToken = default, NavigationOptions? options = null);
        /// <summary>Runs a request after successful activation; the runtime observes completion and failure.</summary>
        void PostPush<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route;
        void PostReplace<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route;
        void PostReset<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route;
        void PostBack (BackOptions? options = null);
        NavigationOperation Push<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route;
        /// <summary>Replaces the selected entry and every entry above it, including their child regions, with one destination.</summary>
        /// <remarks>The target is resolved once within this navigator's region. Missing or ambiguous targets are rejected before changing screens.</remarks>
        NavigationOperation ReplaceFrom<TRoute> (HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route;
        NavigationOperation Reset<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route;
        NavigationOperation Back (BackOptions? options = null);
        NavigationOperation Reload (ReloadOptions? options = null);
        IScreenNavigation GetRegionNavigation (RegionTarget target);
    }
}
