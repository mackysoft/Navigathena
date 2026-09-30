using System;

namespace MackySoft.Navigathena
{
    /// <summary>A typed reply capability belonging to one live screen binding.</summary>
    public sealed class ScreenCall<TResult>
    {
        private readonly Action<TResult> complete;
        private readonly Action dismiss;
        private readonly Func<Route<TResult>, bool, NavigationOptions?, NavigationOperation> change;

        internal ScreenCall (Action<TResult> complete, Action dismiss, Func<Route<TResult>, bool, NavigationOptions?, NavigationOperation> change)
        {
            this.complete = complete;
            this.dismiss = dismiss;
            this.change = change;
        }

        /// <summary>Submits an answer. The runtime stops this screen; never await your own termination.</summary>
        public void Complete (TResult result) => complete(result);
        public void Dismiss () => dismiss();
        public NavigationOperation Replace (Route<TResult> route, NavigationOptions? options = null) => change(route, false, options);
        public NavigationOperation Reset (Route<TResult> route, NavigationOptions? options = null) => change(route, true, options);
    }
}
