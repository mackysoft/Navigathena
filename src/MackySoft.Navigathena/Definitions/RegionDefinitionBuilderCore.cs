using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    internal sealed class RegionDefinitionBuilderCore
    {
        private readonly List<IRouteBuilder> routeBuilders = new();
        private bool closed;

        public RegionDefinitionBuilderCore (RegionDefinitionId id, RegionCompositionMode mode, RegionOccupancy occupancy, RegionDefinitionId? ownerDefinitionId)
        {
            Id = id;
            Mode = mode;
            Occupancy = occupancy;
            OwnerDefinitionId = ownerDefinitionId;
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

        public void AddRoute<TRoute> (Action<RouteDefinitionBuilder<TRoute>> define) where TRoute : NavigationRoute
        {
            EnsureOpen();
            if (define is null)
            {
                throw new ArgumentNullException(nameof(define));
            }

            if (routeBuilders.Any(builder => builder.RouteType == typeof(TRoute)))
            {
                throw new NavigationConfigurationException("A region may register an exact route type only once.");
            }

            RouteDefinitionBuilder<TRoute> builder = new(this);
            try
            {
                define(builder);
                routeBuilders.Add(builder);
            }
            finally
            {
                builder.Close();
            }
        }

        public RegionDefinition Complete ()
        {
            Close();
            List<RegionRouteDefinition> routes = new();
            foreach (IRouteBuilder builder in routeBuilders)
            {
                routes.Add(builder.Complete());
            }

            if (routes.Count == 0)
            {
                throw new NavigationConfigurationException("A region requires at least one route registration.");
            }

            return new RegionDefinition(Id, Mode, Occupancy, OwnerDefinitionId, routes);
        }

        public void Close () => closed = true;

        private void EnsureOpen ()
        {
            if (closed)
            {
                throw new InvalidOperationException("The region definition builder is no longer active.");
            }
        }

        internal interface IRouteBuilder
        {
            Type RouteType
            {
                get;
            }
            RegionRouteDefinition Complete ();
        }
    }

}
