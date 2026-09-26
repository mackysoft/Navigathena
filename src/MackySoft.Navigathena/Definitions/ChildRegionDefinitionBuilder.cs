using System;

namespace MackySoft.Navigathena
{

    /// <summary>Builds the routes accepted by a child region owned by one route registration.</summary>
    public sealed class ChildRegionDefinitionBuilder
    {
        private readonly RegionDefinitionBuilderCore core;

        internal ChildRegionDefinitionBuilder (RegionDefinitionBuilderCore core) => this.core = core;

        /// <summary>Adds an exact route type to this child region.</summary>
        public void AddRoute<TRoute> (Action<RouteDefinitionBuilder<TRoute>> define) where TRoute : NavigationRoute => core.AddRoute(define);
    }

}
