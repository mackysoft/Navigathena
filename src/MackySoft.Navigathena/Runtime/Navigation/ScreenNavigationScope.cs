using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.Runtime.Navigation
{
    /// <summary> Freezes the relative region identities without retaining an obsolete navigation snapshot. </summary>
    internal sealed class ScreenNavigationScope
    {
        private readonly Dictionary<(RegionTargetKind Kind, RegionDefinitionId? First, RegionDefinitionId? Second), RegionInstanceId> targets = new();
        private readonly Dictionary<RegionInstanceId, NavigationEntryId> ownerEntries = new();

        public ScreenNavigationScope (NavigationState proposed, NavigationEntryId entryId)
        {
            NavigationEntry source = proposed.GetEntry(entryId);
            OwnRegion = source.RegionId;
            targets.Add((RegionTargetKind.OwnRegion, null, null), OwnRegion);
            targets.Add((RegionTargetKind.Root, null, null), proposed.RootRegionInstanceId);

            // Child identities belong to the actual owner entry on this path, even when a
            // different entry later becomes current in the same ancestor region.
            NavigationEntry owner = source;
            while (true)
            {
                RegionState region = proposed.GetRegion(owner.RegionId);
                ownerEntries.Add(region.Id, owner.Id);
                targets.Add((RegionTargetKind.AncestorRegion, region.DefinitionId, null), region.Id);
                foreach (RegionState child in proposed.Regions.Values)
                {
                    if (child.OwnerEntryId != owner.Id)
                    {
                        continue;
                    }

                    targets.Add((RegionTargetKind.AncestorChild, region.DefinitionId, child.DefinitionId), child.Id);
                    if (owner.Id == source.Id)
                    {
                        targets.Add((RegionTargetKind.Child, child.DefinitionId, null), child.Id);
                    }
                }

                if (!region.OwnerEntryId.HasValue)
                {
                    break;
                }

                owner = proposed.GetEntry(region.OwnerEntryId.Value);
            }
        }

        public RegionInstanceId OwnRegion
        {
            get;
        }

        public bool TryGetOwner (RegionInstanceId region, out NavigationEntryId owner) => ownerEntries.TryGetValue(region, out owner);

        public RegionInstanceId Resolve (RegionTarget target)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return targets.TryGetValue((target.Kind, target.First, target.Second), out RegionInstanceId region)
                ? region
                : throw new NavigationConfigurationException("The relative region is outside the source screen's logical ownership scope.");
        }
    }
}
