using System;

namespace MackySoft.Navigathena
{
    /// <summary>Construction capabilities supplied by the runtime without exposing its instance registry or ownership implementation.</summary>
    internal abstract class ScreenCreationServices
    {
        public abstract RegionInstanceId RegionId
        {
            get;
        }
        public abstract LifetimeContext Lifetime
        {
            get;
        }
        public abstract void SetLifecycleHandler<TRoute> (IScreenLifecycleHandler<TRoute> handler) where TRoute : Route;
        public abstract void SetLifecycleHandler<TRoute, TResult> (IScreenLifecycleHandler<TRoute, TResult> handler) where TRoute : Route<TResult>;
        public abstract void ConnectPresentation (ScreenPresentationBinding binding);
        public abstract void SetTransitionEffect (INavigationTransitionEffect effect, IViewAdapter adapter);
        public abstract void RegisterScreens (RegionDefinitionId region, Action<RegionScreenCatalogBuilder> configure);
    }
}
