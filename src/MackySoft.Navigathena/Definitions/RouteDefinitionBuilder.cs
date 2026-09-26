using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Builds one exact route registration and the child regions it owns.</summary>
    public sealed class RouteDefinitionBuilder<TRoute> : RegionDefinitionBuilderCore.IRouteBuilder where TRoute : NavigationRoute
    {
        private readonly RegionDefinitionBuilderCore parent;
        private RouteEntryOperations? allowedEntryOperations;
        private LowerPresentationPolicy? lowerPresentationPolicy;
        private readonly List<RegionDefinition> children = new();
        private bool closed;

        internal RouteDefinitionBuilder (RegionDefinitionBuilderCore parent) => this.parent = parent;

        Type RegionDefinitionBuilderCore.IRouteBuilder.RouteType => typeof(TRoute);

        /// <summary>Gets or sets the operations allowed to create a new entry for this route.</summary>
        /// <remarks>Must be assigned during configuration. Back and restoration do not use these permissions.</remarks>
        public RouteEntryOperations AllowedEntryOperations
        {
            get => allowedEntryOperations ?? throw new InvalidOperationException("AllowedEntryOperations has not been configured.");
            set
            {
                EnsureOpen();

                const RouteEntryOperations all = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
                if (value == RouteEntryOperations.None || (value & ~all) != 0)
                {
                    throw new NavigationConfigurationException("AllowedEntryOperations must contain one or more known entry operations.");
                }

                allowedEntryOperations = value;
            }
        }

        /// <summary>Gets or sets how this route affects lower entries in the same region and their descendant screens.</summary>
        /// <remarks>Must be assigned during configuration. It does not affect the owning screen, sibling regions, or this route's own child screens.</remarks>
        public LowerPresentationPolicy LowerPresentationPolicy
        {
            get => lowerPresentationPolicy ?? throw new InvalidOperationException("LowerPresentationPolicy has not been configured.");
            set
            {
                EnsureOpen();
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                value.Validate();
                lowerPresentationPolicy = value;
            }
        }

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
            if (!allowedEntryOperations.HasValue || lowerPresentationPolicy is null)
            {
                throw new NavigationConfigurationException("Each route registration requires AllowedEntryOperations and LowerPresentationPolicy.");
            }

            return new RegionRouteDefinition(new RegionRouteDefinitionKey(parent.Id, typeof(TRoute)), allowedEntryOperations.Value, lowerPresentationPolicy, children);
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
