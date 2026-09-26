using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    /// <summary>Defines a history slot, its occupancy, and its exact route registrations.</summary>
    public sealed class RegionDefinition
    {
        internal RegionDefinition (RegionDefinitionId id, RegionCompositionMode mode, RegionOccupancy occupancy, RegionDefinitionId? ownerDefinitionId, IReadOnlyList<RegionRouteDefinition> routes)
        {
            Id = id;
            Mode = mode;
            Occupancy = occupancy;
            OwnerDefinitionId = ownerDefinitionId;
            Routes = Array.AsReadOnly(routes.ToArray());
        }

        public RegionDefinitionId Id
        {
            get;
        }
        public RegionCompositionMode Mode
        {
            get;
        }
        public RegionOccupancy Occupancy
        {
            get;
        }
        public RegionDefinitionId? OwnerDefinitionId
        {
            get;
        }
        public IReadOnlyList<RegionRouteDefinition> Routes
        {
            get;
        }
    }

}
