using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Provides one immutable snapshot of logical histories and presentation correlations.</summary>
    public sealed class NavigationState
    {
        private readonly IReadOnlyDictionary<RegionInstanceId, RegionState> regions;
        private readonly IReadOnlyDictionary<NavigationEntryId, NavigationEntry> entries;
        private readonly IReadOnlyDictionary<NavigationEntryId, PresentationState> presentations;

        internal NavigationState (long revision, RegionInstanceId rootRegionInstanceId, IDictionary<RegionInstanceId, RegionState> regions, IDictionary<NavigationEntryId, NavigationEntry> entries, IDictionary<NavigationEntryId, PresentationState> presentations, NavigationHostIncident? hostIncident)
        {
            Revision = revision;
            RootRegionInstanceId = rootRegionInstanceId;
            this.regions = new System.Collections.ObjectModel.ReadOnlyDictionary<RegionInstanceId, RegionState>(new Dictionary<RegionInstanceId, RegionState>(regions));
            this.entries = new System.Collections.ObjectModel.ReadOnlyDictionary<NavigationEntryId, NavigationEntry>(new Dictionary<NavigationEntryId, NavigationEntry>(entries));
            this.presentations = new System.Collections.ObjectModel.ReadOnlyDictionary<NavigationEntryId, PresentationState>(new Dictionary<NavigationEntryId, PresentationState>(presentations));
            HostIncident = hostIncident;
        }

        public long Revision
        {
            get;
        }
        public RegionInstanceId RootRegionInstanceId
        {
            get;
        }
        public IReadOnlyDictionary<RegionInstanceId, RegionState> Regions => regions;
        public IReadOnlyDictionary<NavigationEntryId, NavigationEntry> Entries => entries;
        public IReadOnlyDictionary<NavigationEntryId, PresentationState> Presentations => presentations;
        public NavigationHostIncident? HostIncident
        {
            get;
        }

        public RegionState GetRegion (RegionInstanceId id) => regions.TryGetValue(id, out RegionState? region) ? region : throw new ArgumentException("The region instance is not present in this state.", nameof(id));
        public NavigationEntry GetEntry (NavigationEntryId id) => entries.TryGetValue(id, out NavigationEntry? entry) ? entry : throw new ArgumentException("The entry is not present in this state.", nameof(id));
        public PresentationState GetPresentation (NavigationEntryId id) => presentations.TryGetValue(id, out PresentationState? state) ? state : throw new ArgumentException("The entry has no presentation state.", nameof(id));

        internal NavigationState With (long revision, IDictionary<RegionInstanceId, RegionState> regions, IDictionary<NavigationEntryId, NavigationEntry> entries, IDictionary<NavigationEntryId, PresentationState> presentations, NavigationHostIncident? hostIncident) => new(revision, RootRegionInstanceId, regions, entries, presentations, hostIncident);
        internal static NavigationState CreateEmpty (RegionDefinitionId rootDefinitionId)
        {
            RegionInstanceId root = new(Guid.NewGuid());
            Dictionary<RegionInstanceId, RegionState> regions = new()
            {
                [root] = new RegionState(root, rootDefinitionId, null, Array.Empty<NavigationEntryId>())
            };
            return new NavigationState(0, root, regions, new Dictionary<NavigationEntryId, NavigationEntry>(), new Dictionary<NavigationEntryId, PresentationState>(), null);
        }
    }

}
