using System;

namespace MackySoft.Navigathena
{
    /// <summary>Acquires dependencies and connects views during construction. The factory returns the screen's single lifecycle handler.</summary>
    public class ScreenCreationContext
    {
        private readonly ScreenCreationServices creation;
        internal ScreenCreationContext (ScreenCreationServices creation) => this.creation = creation;

        public RegionInstanceId RegionId => creation.RegionId;
        public ResourcePreparationContext Resources => creation.Resources;
        internal void ConnectPresentation (ScreenPresentationBinding binding) => creation.ConnectPresentation(binding);
        public void SetTransitionEffect (INavigationTransitionEffect effect, IViewAdapter adapter) => creation.SetTransitionEffect(effect, adapter);
        public void RegisterScreens (RegionDefinitionId region, Action<RegionScreenCatalogBuilder> configure) => creation.RegisterScreens(region, configure);
    }

    /// <summary>Preserves the route contract for lifecycle and dependency injection construction.</summary>
    public class ScreenCreationContext<TRoute> : ScreenCreationContext where TRoute : NavigationRoute
    {
        internal ScreenCreationContext (ScreenCreationServices creation) : base(creation)
        {
        }
    }
}
