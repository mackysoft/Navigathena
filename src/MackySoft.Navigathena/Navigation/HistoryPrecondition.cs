using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{
    internal sealed class HistoryPrecondition
    {
        private readonly NavigationState snapshot;
        private readonly IReadOnlyList<RegionState> regions;

        public HistoryPrecondition (NavigationEntryId entryId, NavigationState snapshot, RegionInstanceId region)
        {
            EntryId = entryId;
            this.snapshot = snapshot;
            regions = snapshot.Regions.Values.Where(candidate => IsWithin(candidate.Id, region, snapshot)).ToArray();
        }

        public NavigationEntryId EntryId
        {
            get;
        }

        public bool IsCurrent (NavigationState state)
        {
            return regions.All(before => state.Regions.TryGetValue(before.Id, out RegionState? current)
                && before.Entries.SequenceEqual(current.Entries)
                && before.Entries.All(id => state.Presentations.TryGetValue(id, out PresentationState? presentation)
                    && Equals(snapshot.GetPresentation(id), presentation)
                    && Equals(snapshot.GetEntry(id), state.GetEntry(id))));
        }

        private static bool IsWithin (RegionInstanceId candidate, RegionInstanceId target, NavigationState state)
        {
            while (candidate != target)
            {
                if (state.GetRegion(candidate).OwnerEntryId is not NavigationEntryId owner)
                {
                    return false;
                }
                candidate = state.GetEntry(owner).RegionId;
            }
            return true;
        }
    }
}
