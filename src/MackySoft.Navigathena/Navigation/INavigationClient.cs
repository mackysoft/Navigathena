using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Defines fundamental operations against explicit regions, independently of screen activity. Extension methods await transition completion.</summary>
    public interface INavigationClient
    {
        /// <summary>Waits for the ordinary screen call to close and release its resources. Cancellation ends the call.</summary>
        Task InvokeAsync (RegionInstanceId target, Route route, CancellationToken cancellationToken = default, NavigationOptions? options = null);
        /// <summary>Returns the answer after callee cleanup. Closing without an answer cancels the task; failures throw.</summary>
        Task<TResult> InvokeAsync<TResult> (RegionInstanceId target, Route<TResult> route, CancellationToken cancellationToken = default, NavigationOptions? options = null);
        NavigationOperation Push<TRoute> (RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route;
        /// <summary>Replaces the selected entry and every entry above it, including their child regions, with one destination.</summary>
        /// <remarks>The target is resolved once within the specified region. Missing or ambiguous targets are rejected before changing screens.</remarks>
        NavigationOperation ReplaceFrom<TRoute> (RegionInstanceId region, HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route;
        NavigationOperation Reset<TRoute> (RegionInstanceId target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route;
        NavigationOperation Back (RegionInstanceId target, BackOptions? options = null);
        NavigationOperation Reload (RegionInstanceId target, ReloadOptions? options = null);
        NavigationOperation Clear (RegionInstanceId target);
    }
}
