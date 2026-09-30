using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    internal sealed class DefinitionBuildContext
    {
        private readonly List<RegionDefinition> regions = new();
        private readonly HashSet<RegionDefinitionId> ids = new();

        public IReadOnlyList<RegionDefinition> Regions => regions.AsReadOnly();

        public void Add (RegionDefinition region)
        {
            if (!ids.Add(region.Id))
            {
                throw new NavigationConfigurationException("A region definition identifier may occur only once.");
            }

            regions.Add(region);
            foreach (RegionRouteDefinition route in region.Routes)
            {
                foreach (RegionDefinition child in route.ChildRegions)
                {
                    Add(child);
                }
            }
        }

        public void Validate (RegionDefinitionId rootId)
        {
            RegionDefinition root = regions.Single(region => region.Id == rootId);
            if (!root.Routes.Any(route => (route.AllowedEntryOperations & RouteEntryOperations.Reset) != 0))
            {
                throw new NavigationConfigurationException("The root region requires at least one route that permits Reset.");
            }
        }
    }

}
