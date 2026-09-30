using System;

namespace MackySoft.Navigathena
{

    /// <summary>Builds the routes accepted by a child region owned by one route registration.</summary>
    public sealed class ChildRegionDefinitionBuilder
    {
        private readonly RegionDefinitionBuilderCore core;

        internal ChildRegionDefinitionBuilder (RegionDefinitionBuilderCore core) => this.core = core;

        /// <summary>Adds an exact route type to this child region.</summary>
        /// <param name="allowedEntryOperations">One or more operations permitted to create a new entry. Back and restoration do not use these permissions.</param>
        /// <param name="lowerPresentationPolicy">How this route affects lower entries in this region and their descendants, not the owning screen or sibling regions.</param>
        /// <param name="defineChildren">Optional definitions of child regions owned by this route.</param>
        public void AddRoute<TRoute> (RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, Action<RouteDefinitionBuilder<TRoute>>? defineChildren = null) where TRoute : NavigationRoute => core.AddRoute(allowedEntryOperations, lowerPresentationPolicy, defineChildren);
    }

}
