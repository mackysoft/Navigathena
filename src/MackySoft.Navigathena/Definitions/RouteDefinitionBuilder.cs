using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Builds one exact route registration and the child regions it owns.</summary>
    public sealed class RouteDefinitionBuilder<TRoute> : RegionDefinitionBuilderCore.IRouteBuilder where TRoute : NavigationRoute
    {
        private readonly RegionDefinitionBuilderCore parent;
        private readonly RouteEntryOperations allowedEntryOperations;
        private readonly LowerPresentationPolicy lowerPresentationPolicy;
        private readonly List<RegionDefinition> children = new();
        private bool closed;

        internal RouteDefinitionBuilder (RegionDefinitionBuilderCore parent, RouteEntryOperations allowedEntryOperations, LowerPresentationPolicy lowerPresentationPolicy)
        {
            const RouteEntryOperations all = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
            if (allowedEntryOperations == RouteEntryOperations.None || (allowedEntryOperations & ~all) != 0)
            {
                throw new NavigationConfigurationException("AllowedEntryOperations must contain one or more known entry operations.");
            }
            if (lowerPresentationPolicy is null)
            {
                throw new ArgumentNullException(nameof(lowerPresentationPolicy));
            }
            lowerPresentationPolicy.Validate();
            this.parent = parent;
            this.allowedEntryOperations = allowedEntryOperations;
            this.lowerPresentationPolicy = lowerPresentationPolicy;
        }

        Type RegionDefinitionBuilderCore.IRouteBuilder.RouteType => typeof(TRoute);

        /// <summary>Adds a child region owned by each entry created for this route.</summary>
        public void AddChildRegion (RegionDefinitionId id, RegionCompositionMode mode, RegionOccupancy occupancy, Action<ChildRegionDefinitionBuilder> define)
        {
            EnsureOpen();
            if (define is null)
            {
                throw new ArgumentNullException(nameof(define));
            }

            if (!Enum.IsDefined(typeof(RegionCompositionMode), mode) || !Enum.IsDefined(typeof(RegionOccupancy), occupancy))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            RegionDefinitionBuilderCore childCore = new(id, mode, occupancy, parent.Id);
            ChildRegionDefinitionBuilder childBuilder = new(childCore);
            try
            {
                define(childBuilder);
                RegionDefinition child = childCore.Complete();
                children.Add(child);
            }
            finally
            {
                childCore.Close();
            }
        }

        RegionRouteDefinition RegionDefinitionBuilderCore.IRouteBuilder.Complete ()
        {
            return new RegionRouteDefinition(new RegionRouteDefinitionKey(parent.Id, typeof(TRoute)), allowedEntryOperations, lowerPresentationPolicy, children);
        }

        internal void Close () => closed = true;

        private void EnsureOpen ()
        {
            if (closed)
            {
                throw new InvalidOperationException("The route definition builder is no longer active.");
            }
        }
    }

}
