using System;

namespace MackySoft.Navigathena
{

    /// <summary>Builds the root region of one immutable navigation definition.</summary>
    public sealed class RootRegionDefinitionBuilder
    {
        private readonly RegionDefinitionBuilderCore core;

        internal RootRegionDefinitionBuilder (RegionDefinitionId id, RegionCompositionMode mode) => core = new RegionDefinitionBuilderCore(id, mode, RegionOccupancy.Required, null);

        /// <summary>Adds an exact route type to the root region.</summary>
        public void AddRoute<TRoute> (Action<RouteDefinitionBuilder<TRoute>> define) where TRoute : NavigationRoute => core.AddRoute(define);
        internal RegionDefinition Complete () => core.Complete();
        internal void Close () => core.Close();
    }

}
