using System;

namespace MackySoft.Navigathena
{
    /// <summary>Acquires dependencies and connects views during construction. The factory returns the screen's single lifecycle handler.</summary>
    public class ScreenCreationContext
    {
        private readonly ScreenCreationServices creation;
        internal ScreenCreationContext (ScreenCreationServices creation) => this.creation = creation;

        public RegionInstanceId RegionId => creation.RegionId;
        /// <summary>Ownership and borrowing for this screen instance. Registration is available during construction.</summary>
        public LifetimeContext Lifetime => creation.Lifetime;
        /// <summary>Reports construction progress until the factory completes. Each acquired resource receives its own work identifier.</summary>
        public NavigationProgressReporter Progress => creation.Progress;
        internal void ConnectPresentation (ScreenPresentationBinding binding) => creation.ConnectPresentation(binding);
        public void SetTransitionEffect (INavigationTransitionEffect effect, IViewAdapter adapter) => creation.SetTransitionEffect(effect, adapter, null);

        /// <summary>Connects a screen-owned effect to a fresh typed progress state whenever it is selected for a transition.</summary>
        /// <remarks>The effect remains screen-owned; the runtime owns each operation's state and observation. A destination-owned effect cannot observe work preceding its construction.</remarks>
        public void SetTransitionEffect<TState> (INavigationTransitionEffect effect, IViewAdapter adapter, ProgressDefinition<TState> progress, Action<TState> receive)
        {
            if (progress is null)
            {
                throw new ArgumentNullException(nameof(progress));
            }
            if (receive is null)
            {
                throw new ArgumentNullException(nameof(receive));
            }
            creation.SetTransitionEffect(effect, adapter, context => context.ObserveProgress(context.CreateProgressSource(progress), receive));
        }
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
