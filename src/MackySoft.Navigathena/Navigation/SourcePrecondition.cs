using System.Linq;

namespace MackySoft.Navigathena
{

    internal sealed class SourcePrecondition
    {
        public SourcePrecondition (NavigationEntryId entryId, PresentationId presentationId, RegionInstanceId? targetRegion = null, NavigationEntryId? expectedTop = null)
        {
            EntryId = entryId;
            PresentationId = presentationId;
            TargetRegion = targetRegion;
            ExpectedTop = expectedTop;
        }

        public NavigationEntryId EntryId
        {
            get;
        }
        public PresentationId PresentationId
        {
            get;
        }
        public RegionInstanceId? TargetRegion
        {
            get;
        }
        public NavigationEntryId? ExpectedTop
        {
            get;
        }

        public bool IsCurrent (NavigationState state)
        {
            if (!state.Presentations.TryGetValue(EntryId, out PresentationState? presentation) || presentation.Materialization != PresentationMaterialization.Available || presentation.Id != PresentationId)
            {
                return false;
            }

            return !TargetRegion.HasValue || (state.Regions.TryGetValue(TargetRegion.Value, out RegionState? region) && region.Entries.LastOrDefault().Equals(ExpectedTop.GetValueOrDefault()));
        }
    }

}
