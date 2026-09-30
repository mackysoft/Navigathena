using System;

namespace MackySoft.Navigathena
{

    /// <summary>Builds the root region of one immutable navigation definition.</summary>
    public sealed class RootRegionDefinitionBuilder
    {
        private readonly RegionDefinitionBuilderCore core;

        internal RootRegionDefinitionBuilder (RegionDefinitionId id, RegionCompositionMode mode) => core = new RegionDefinitionBuilderCore(id, mode, RegionOccupancy.Required, null);

        /// <summary>Adds an exact route type to the root region.</summary>
        /// <param name="allowedEntryOperations">One or more operations permitted to create a new entry. Back and restoration do not use these permissions.</param>
        /// <param name="lowerPresentationPolicy">How this route affects lower entries in this region and their descendants, not sibling regions.</param>
        /// <param name="defineChildren">Optional definitions of child regions owned by this route.</param>
        public void AddRoute<TRoute> (RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, Action<RouteDefinitionBuilder<TRoute>>? defineChildren = null) where TRoute : NavigationRoute => core.AddRoute(allowedEntryOperations, lowerPresentationPolicy, defineChildren);
        internal RegionDefinition Complete () => core.Complete();
        internal void Close () => core.Close();
    }

}
