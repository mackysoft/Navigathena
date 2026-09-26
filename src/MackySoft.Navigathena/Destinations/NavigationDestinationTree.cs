using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Represents a typed route destination and its immutable initial child configuration.</summary>
    public sealed class NavigationDestinationTree<TRoute> : INavigationDestinationTree where TRoute : NavigationRoute
    {
        private readonly IReadOnlyDictionary<RegionDefinitionId, INavigationDestinationTree> children;

        internal NavigationDestinationTree (TRoute route, IDictionary<RegionDefinitionId, INavigationDestinationTree>? children = null)
        {
            Route = route ?? throw new ArgumentNullException(nameof(route));
            this.children = new System.Collections.ObjectModel.ReadOnlyDictionary<RegionDefinitionId, INavigationDestinationTree>(children is null ? new Dictionary<RegionDefinitionId, INavigationDestinationTree>() : new Dictionary<RegionDefinitionId, INavigationDestinationTree>(children));
        }

        public TRoute Route
        {
            get;
        }
        NavigationRoute INavigationDestinationTree.Route => Route;
        public IReadOnlyDictionary<RegionDefinitionId, INavigationDestinationTree> Children => children;

        /// <summary>Returns a new destination with the initial destination for one child region.</summary>
        public NavigationDestinationTree<TRoute> Child<TChildRoute> (RegionDefinitionId childRegion, NavigationDestinationTree<TChildRoute> child) where TChildRoute : Route
        {
            if (child is null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            if (children.ContainsKey(childRegion))
            {
                throw new ArgumentException("A child region destination may be specified once.", nameof(childRegion));
            }

            Dictionary<RegionDefinitionId, INavigationDestinationTree> nextChildren = new(children)
            {
                [childRegion] = child
            };
            return new NavigationDestinationTree<TRoute>(Route, nextChildren);
        }
    }

}
