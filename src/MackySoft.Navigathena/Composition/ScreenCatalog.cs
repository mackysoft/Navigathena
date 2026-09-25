using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{
    /// <summary>Maps region and route definitions to asynchronous screen construction.</summary>
    public sealed class ScreenCatalog
    {
        private readonly IReadOnlyDictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> factories;

        private ScreenCatalog (NavigationDefinition definition, Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> factories)
        {
            Definition = definition;
            this.factories = factories;
        }

        public NavigationDefinition Definition
        {
            get;
        }
        internal IReadOnlyDictionary<RegionRouteDefinitionKey, BlockerDefinition> Blockers { get; private set; } = new Dictionary<RegionRouteDefinitionKey, BlockerDefinition>();

        /// <summary>Defines a layered root and its screen construction together.</summary>
        public static ScreenCatalog Build (Action<ScreenCatalogDefinitionBuilder> configure)
            => Build(new RegionDefinitionId("root"), RegionCompositionMode.Layered, configure);

        public static ScreenCatalog Build (RegionDefinitionId rootId, RegionCompositionMode mode, Action<ScreenCatalogDefinitionBuilder> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ScreenCatalogDefinitionBuilder builder = new();
            try
            {
                configure(builder);
                return builder.BuildRoot(rootId, mode);
            }
            finally
            {
                builder.Close();
            }
        }

        public static ScreenCatalog Build (NavigationDefinition definition, Action<ScreenCatalogBuilder> configure)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            ScreenCatalogBuilder builder = new(definition);
            try
            {
                configure(builder);
                return new ScreenCatalog(definition, builder.Complete()) { Blockers = new Dictionary<RegionRouteDefinitionKey, BlockerDefinition>(builder.Blockers) };
            }
            finally
            {
                builder.Close();
            }
        }

        internal bool TryResolve (RegionRouteDefinitionKey key, out Func<NavigationRoute, ScreenDefinition> resolve) => factories.TryGetValue(key, out resolve!);

        internal Func<NavigationRoute, ScreenDefinition> Resolve (RegionRouteDefinitionKey key) => factories.TryGetValue(key, out Func<NavigationRoute, ScreenDefinition>? factory)
            ? factory
            : throw new NavigationConfigurationException("No screen factory is registered for " + key + ".");
    }
}
