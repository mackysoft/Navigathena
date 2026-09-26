using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{
    /// <summary>Registers screen factories in independent feature configuration callbacks.</summary>
    public sealed class ScreenCatalogBuilder
    {
        private readonly NavigationDefinition definition;
        private readonly Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> factories = new();
        private bool closed;
        private bool configuring;
        internal Dictionary<RegionRouteDefinitionKey, BlockerDefinition> Blockers { get; } = new();

        internal ScreenCatalogBuilder (NavigationDefinition definition) => this.definition = definition;

        public void RegisterScreens (RegionDefinitionId region, Action<RegionScreenCatalogBuilder> configure)
        {
            if (closed || configuring)
            {
                throw new InvalidOperationException("The catalog configuration callback has ended.");
            }

            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> staged = new(factories);
            Dictionary<RegionRouteDefinitionKey, BlockerDefinition> stagedBlockers = new(Blockers);
            RegionScreenCatalogBuilder builder = new(definition.GetRegion(region), staged, stagedBlockers);
            configuring = true;
            try
            {
                configure(builder);
                foreach (var pair in staged)
                {
                    factories[pair.Key] = pair.Value;
                }

                foreach (var pair in stagedBlockers)
                {
                    Blockers[pair.Key] = pair.Value;
                }
            }
            finally
            {
                builder.Close();
                configuring = false;
            }
        }

        internal Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> Complete ()
        {
            foreach (RegionRouteDefinition route in definition.GetRegion(definition.RootRegionId).Routes)
            {
                if (!factories.ContainsKey(route.Key))
                {
                    throw new NavigationConfigurationException("No root screen factory is registered for " + route.Key + ".");
                }
            }

            return new Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>>(factories);
        }

        internal void Close () => closed = true;
    }
}
