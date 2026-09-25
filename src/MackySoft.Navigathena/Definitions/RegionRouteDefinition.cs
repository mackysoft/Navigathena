using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    /// <summary>Defines how one exact route type enters a region and affects lower presentations.</summary>
    public sealed class RegionRouteDefinition
    {
        internal RegionRouteDefinition (RegionRouteDefinitionKey key, RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy, IReadOnlyList<RegionDefinition> childRegions)
        {
            Key = key;
            AllowedEntryOperations = allowedEntryOperations;
            LowerPresentationPolicy = lowerPresentationPolicy;
            ChildRegions = Array.AsReadOnly(childRegions.ToArray());
        }

        public RegionRouteDefinitionKey Key
        {
            get;
        }
        /// <summary>Gets the operations allowed to create a new entry for this route.</summary>
        public RouteEntryOperations AllowedEntryOperations
        {
            get;
        }
        /// <summary>Gets this route's effects on lower presentations, not its own presentation.</summary>
        public LowerPresentationPolicy LowerPresentationPolicy
        {
            get;
        }
        public IReadOnlyList<RegionDefinition> ChildRegions
        {
            get;
        }
    }

}
