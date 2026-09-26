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

        public void Register<TRoute> (Func<ScreenCreationContext<TRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute>>> create, Action<RouteDefinitionBuilder<TRoute>> configure) where TRoute : Route
            => Register(new ScreenDefinition<TRoute>(create), configure);

        public void Register<TRoute, TResult> (Func<ScreenCreationContext<TRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute, TResult>>> create, Action<RouteDefinitionBuilder<TRoute>> configure) where TRoute : Route<TResult>
            => Register(new ScreenDefinition<TRoute, TResult>(create), configure);

        public void Register<TRoute> (ScreenDefinition<TRoute> screen, Action<RouteDefinitionBuilder<TRoute>> configure) where TRoute : Route
            => RegisterRoot(screen, configure, screens => screens.RegisterScreen(screen));

        public void Register<TRoute, TResult> (ScreenDefinition<TRoute, TResult> screen, Action<RouteDefinitionBuilder<TRoute>> configure) where TRoute : Route<TResult>
            => RegisterRoot(screen, configure, screens => screens.RegisterScreen(screen));

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

        private void RegisterRoot<TRoute> (ScreenDefinition screen, Action<RouteDefinitionBuilder<TRoute>> configure, Action<RegionScreenCatalogBuilder> register) where TRoute : NavigationRoute
        {
            EnsureOpen();
            if (screen is null)
            {
                throw new ArgumentNullException(nameof(screen));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            routeRegistrations.Add(root => root.AddRoute(configure));
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
