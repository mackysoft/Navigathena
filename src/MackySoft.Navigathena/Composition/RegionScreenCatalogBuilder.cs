using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{
    /// <summary>Registers construction for routes belonging to one region definition.</summary>
    public sealed class RegionScreenCatalogBuilder
    {
        private readonly RegionDefinition region;
        private readonly Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> factories;
        private bool closed;
        private readonly Dictionary<RegionRouteDefinitionKey, BlockerDefinition> blockers;

        internal RegionScreenCatalogBuilder (RegionDefinition region, Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> factories, Dictionary<RegionRouteDefinitionKey, BlockerDefinition> blockers)
        {
            this.region = region;
            this.factories = factories;
            this.blockers = blockers;
        }

        /// <summary>Registers a shared screen construction definition without executing its factory.</summary>
        public void RegisterScreen<TRoute> (ScreenDefinition<TRoute> definition) where TRoute : Route
            => RegisterScreen<TRoute>((ScreenDefinition)definition);

        public void RegisterScreen<TRoute, TResult> (ScreenDefinition<TRoute, TResult> definition) where TRoute : Route<TResult>
            => RegisterScreen<TRoute>((ScreenDefinition)definition);

        /// <summary>Shares a construction definition across registrations accepting compatible route types.</summary>
        public void RegisterScreen<TRoute> (ScreenDefinition definition) where TRoute : NavigationRoute
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (!definition.RouteType.IsAssignableFrom(typeof(TRoute)))
            {
                throw new NavigationConfigurationException("The construction definition cannot handle this route type.");
            }
            RegisterScreen<TRoute>(_ => definition);
        }

        /// <summary>Selects stable construction identity from the route before physical preparation.</summary>
        public void RegisterScreen<TRoute> (Func<TRoute, ScreenDefinition> resolve) where TRoute : NavigationRoute
        {
            if (closed)
            {
                throw new InvalidOperationException("The region registration callback has ended.");
            }
            if (resolve is null)
            {
                throw new ArgumentNullException(nameof(resolve));
            }
            RegionRouteDefinitionKey key = new(region.Id, typeof(TRoute));
            if (!region.Routes.Any(route => route.Key.Equals(key)))
            {
                throw new NavigationConfigurationException("The route is not defined in this region: " + key + ".");
            }
            if (factories.ContainsKey(key))
            {
                throw new NavigationConfigurationException("The screen is already registered: " + key + ".");
            }
            factories.Add(key, route =>
            {
                ScreenDefinition selected = resolve((TRoute)route) ?? throw new NavigationConfigurationException("The screen selector returned null.");
                if (!selected.RouteType.IsInstanceOfType(route))
                {
                    throw new NavigationConfigurationException("The selected construction definition cannot handle this route type.");
                }
                return selected;
            });
        }

        internal void Close () => closed = true;

        /// <summary>Selects a reusable blocker without assigning ownership to this region or route.</summary>
        public void RegisterBlocker<TRoute> (BlockerDefinition definition) where TRoute : NavigationRoute
        {
            if (closed)
            {
                throw new InvalidOperationException("The region registration callback has ended.");
            }

            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            RegionRouteDefinitionKey key = new(region.Id, typeof(TRoute));
            if (!region.Routes.Any(route => route.Key.Equals(key)))
            {
                throw new NavigationConfigurationException("The route is not defined in this region.");
            }

            if (blockers.ContainsKey(key))
            {
                throw new NavigationConfigurationException("The blocker is already registered for this route.");
            }

            blockers.Add(key, definition);
        }
    }
}
