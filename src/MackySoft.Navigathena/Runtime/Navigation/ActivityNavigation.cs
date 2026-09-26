using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Navigation
{
    internal sealed class ActivityNavigation : IScreenNavigation
    {
        private readonly IScreenNavigation navigation;
        private readonly Func<bool> isActive;
        private readonly INavigationStateSource state;
        private readonly Func<bool> isCurrent;

        public ActivityNavigation (IScreenNavigation navigation, Func<bool> isActive, INavigationStateSource state, Func<bool>? isCurrent = null)
        {
            this.navigation = navigation;
            this.isActive = isActive;
            this.state = state;
            this.isCurrent = isCurrent ?? isActive;
        }

        public Task InvokeAsync (Route route, CancellationToken cancellationToken = default, NavigationOptions? options = null)
        {
            ValidateCall();
            return navigation.InvokeAsync(route, cancellationToken, options);
        }

        public Task<TResult> InvokeAsync<TResult> (Route<TResult> route, CancellationToken cancellationToken = default, NavigationOptions? options = null)
        {
            ValidateCall();
            return navigation.InvokeAsync(route, cancellationToken, options);
        }

        private void ValidateCall ()
        {
            if (NavigationCallbackScope.IsExecuting || !isActive())
            {
                throw new InvalidOperationException("Screen calls require a live activity or owned work outside a lifecycle callback.");
            }
        }

        public NavigationOperation Push<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Request(NavigationOperationKind.Push, () => navigation.Push(destination, options));
        public NavigationOperation ReplaceFrom<TRoute> (HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Request(NavigationOperationKind.Replace, () => navigation.ReplaceFrom(target, destination, options));
        public NavigationOperation Reset<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Request(NavigationOperationKind.Reset, () => navigation.Reset(destination, options));
        public NavigationOperation Back (BackOptions? options = null) => Request(NavigationOperationKind.Back, () => navigation.Back(options));
        public NavigationOperation Reload (ReloadOptions? options = null) => Request(NavigationOperationKind.Reload, () => navigation.Reload(options));
        public void PostPush<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => Post(() => navigation.PostPush(route, options));
        public void PostReplace<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => Post(() => navigation.PostReplace(route, options));
        public void PostReset<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => Post(() => navigation.PostReset(route, options));
        public void PostBack (BackOptions? options = null) => Post(() => navigation.PostBack(options));
        private void Post (Action request)
        {
            if (!isCurrent())
            {
                throw new InvalidOperationException("The activity or owned work that posts navigation is no longer current.");
            }
            request();
        }
        public IScreenNavigation GetRegionNavigation (RegionTarget target) => new ActivityNavigation(navigation.GetRegionNavigation(target), isActive, state, isCurrent);


        private NavigationOperation Request (NavigationOperationKind kind, Func<NavigationOperation> request)
        {
            if (NavigationCallbackScope.IsExecuting)
            {
                throw new InvalidOperationException("Navigation cannot be requested from a lifecycle callback.");
            }
            if (!isActive())
            {
                return NavigationOperation.FromResult(new NavigationResult(new NavigationOperationId(Guid.NewGuid()), kind, NavigationResultKind.Rejected, false, state.Current, NavigationDelta.Empty, RestorationOutcome.NotRequired,
                    new[] { new NavigationDiagnostic(NavigationPhase.Prepare, "Navigation requires a current activity.") }));
            }

            return request();
        }
    }
}
