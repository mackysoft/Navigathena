using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    /// <summary>Contains the immutable region and route structure used by one navigation host.</summary>
    public sealed class NavigationDefinition
    {
        private readonly Dictionary<RegionDefinitionId, RegionDefinition> regions;
        private readonly Dictionary<RegionRouteDefinitionKey, RegionRouteDefinition> routes;

        private NavigationDefinition (RegionDefinitionId rootRegionId, IReadOnlyList<RegionDefinition> regions)
        {
            RootRegionId = rootRegionId;
            Regions = Array.AsReadOnly(regions.ToArray());
            this.regions = regions.ToDictionary(static region => region.Id);
            routes = regions.SelectMany(static region => region.Routes).ToDictionary(static route => route.Key);
        }

        public RegionDefinitionId RootRegionId
        {
            get;
        }
        public IReadOnlyList<RegionDefinition> Regions
        {
            get;
        }

        public static NavigationDefinition Build (RegionDefinitionId rootId, RegionCompositionMode mode, Action<RootRegionDefinitionBuilder> define)
        {
            if (define is null)
            {
                throw new ArgumentNullException(nameof(define));
            }

            if (!Enum.IsDefined(typeof(RegionCompositionMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            RootRegionDefinitionBuilder builder = new(rootId, mode);
            try
            {
                define(builder);
                RegionDefinition root = builder.Complete();
                DefinitionBuildContext context = new();
                context.Add(root);
                context.Validate(rootId);
                return new NavigationDefinition(rootId, context.Regions);
            }
            finally
            {
                builder.Close();
            }
        }

        public RegionDefinition GetRegion (RegionDefinitionId id) => regions.TryGetValue(id, out RegionDefinition? region) ? region : throw new ArgumentException("The region is not registered.", nameof(id));

        public bool TryGetRoute (RegionDefinitionId regionId, Type routeType, out RegionRouteDefinition? route)
        {
            bool found = routes.TryGetValue(new RegionRouteDefinitionKey(regionId, routeType), out RegionRouteDefinition? resolved);
            route = resolved;
            return found;
        }

        internal RegionRouteDefinition GetRoute (RegionDefinitionId regionId, NavigationRoute route)
        {
            if (TryGetRoute(regionId, route.GetType(), out RegionRouteDefinition? definition) && definition is not null)
            {
                return definition;
            }

            throw new NavigationConfigurationException("The destination route is not registered for its target region.");
        }
    }

}
