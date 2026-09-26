using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies a route registration by its region definition and exact route type.</summary>
    public readonly struct RegionRouteDefinitionKey : IEquatable<RegionRouteDefinitionKey>
    {
        public RegionRouteDefinitionKey (RegionDefinitionId regionId, Type routeType)
        {
            if (routeType is null)
            {
                throw new ArgumentNullException(nameof(routeType));
            }

            RegionId = regionId;
            RouteType = routeType;
        }

        public RegionDefinitionId RegionId
        {
            get;
        }
        public Type RouteType
        {
            get;
        }
        public bool Equals (RegionRouteDefinitionKey other) => RegionId.Equals(other.RegionId) && RouteType == other.RouteType;
        public override bool Equals (object? obj) => obj is RegionRouteDefinitionKey other && Equals(other);
        public override int GetHashCode () => HashCode.Combine(RegionId, RouteType);
        public override string ToString () => RegionId + ":" + RouteType.FullName;
    }

}
