using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Defines root routes and their construction, then binds them through the catalog's common registration rules.</summary>
    public sealed class ScreenCatalogDefinitionBuilder
    {
        private readonly List<Action<RootRegionDefinitionBuilder>> routeRegistrations = new();
        private readonly List<Action<RegionScreenCatalogBuilder>> rootRegistrations = new();
        private readonly List<Action<ScreenCatalogBuilder>> regionRegistrations = new();
        private bool closed;

        internal ScreenCatalogDefinitionBuilder ()
        {
        }

        /// <summary>Registers a root route with its entry permissions, lower-screen policy, and construction factory.</summary>
        /// <param name="allowedEntryOperations">One or more operations permitted to create a new entry. Back and restoration are unaffected.</param>
        /// <param name="lowerPresentationPolicy">How the route affects lower entries in the root region and their descendants.</param>
        /// <param name="create">Constructs one screen instance; Route values are supplied to its lifecycle handler.</param>
        /// <param name="defineChildren">Optional child-region definitions for this route.</param>
        public void Register<TRoute> (RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, Func<ScreenCreationContext<TRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute>>> create, Action<RouteDefinitionBuilder<TRoute>>? defineChildren = null) where TRoute : Route
            => Register(allowedEntryOperations, lowerPresentationPolicy, new ScreenDefinition<TRoute>(create), defineChildren);

        /// <summary>Registers a result-returning root route. Entry permissions apply within the call boundary.</summary>
        public void Register<TRoute, TResult> (RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, Func<ScreenCreationContext<TRoute, TResult>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute, TResult>>> create, Action<RouteDefinitionBuilder<TRoute>>? defineChildren = null) where TRoute : Route<TResult>
            => Register(allowedEntryOperations, lowerPresentationPolicy, new ScreenDefinition<TRoute, TResult>(create), defineChildren);

        /// <summary>Registers a root route using an existing, shareable screen construction definition.</summary>
        public void Register<TRoute> (RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, ScreenDefinition<TRoute> screen, Action<RouteDefinitionBuilder<TRoute>>? defineChildren = null) where TRoute : Route
            => RegisterRoot(screen, allowedEntryOperations, lowerPresentationPolicy, defineChildren, screens => screens.RegisterScreen(screen));

        /// <summary>Registers a result-returning root route using an existing, shareable screen construction definition.</summary>
        public void Register<TRoute, TResult> (RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, ScreenDefinition<TRoute, TResult> screen, Action<RouteDefinitionBuilder<TRoute>>? defineChildren = null) where TRoute : Route<TResult>
            => RegisterRoot(screen, allowedEntryOperations, lowerPresentationPolicy, defineChildren, screens => screens.RegisterScreen(screen));

        /// <summary>Registers construction and blockers for a region after its route definitions have been built.</summary>
        public void RegisterScreens (RegionDefinitionId region, Action<RegionScreenCatalogBuilder> configure)
        {
            EnsureOpen();
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            regionRegistrations.Add(catalog => catalog.RegisterScreens(region, configure));
        }

        private void RegisterRoot<TRoute> (ScreenDefinition screen, RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, Action<RouteDefinitionBuilder<TRoute>>? defineChildren, Action<RegionScreenCatalogBuilder> register) where TRoute : NavigationRoute
        {
            EnsureOpen();
            if (screen is null)
            {
                throw new ArgumentNullException(nameof(screen));
            }
            routeRegistrations.Add(root => root.AddRoute(allowedEntryOperations, lowerPresentationPolicy, defineChildren));
            rootRegistrations.Add(register);
        }

        internal ScreenCatalog BuildRoot (RegionDefinitionId rootId, RegionCompositionMode mode)
        {
            Close();
            NavigationDefinition navigation = NavigationDefinition.Build(rootId, mode, root =>
            {
                foreach (Action<RootRegionDefinitionBuilder> register in routeRegistrations)
                {
                    register(root);
                }
            });
            return ScreenCatalog.Build(navigation, catalog =>
            {
                catalog.RegisterScreens(rootId, screens =>
                {
                    foreach (Action<RegionScreenCatalogBuilder> register in rootRegistrations)
                    {
                        register(screens);
                    }
                });
                foreach (Action<ScreenCatalogBuilder> register in regionRegistrations)
                {
                    register(catalog);
                }
            });
        }

        internal void Close () => closed = true;

        private void EnsureOpen ()
        {
            if (closed)
            {
                throw new InvalidOperationException("The catalog definition callback has ended.");
            }
        }
    }
}
