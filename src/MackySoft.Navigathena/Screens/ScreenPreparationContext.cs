namespace MackySoft.Navigathena
{
    /// <summary>Input-specific preparation and resources for one displayed history entry; never a DI registration context.</summary>
    public sealed class ScreenPreparationContext
    {
        internal ScreenPreparationContext (NavigationEntry entry, ResourcePreparationContext resources, ScreenPreparationReason reason)
        {
            EntryId = entry.Id;
            RegionId = entry.RegionId;
            SavedState = entry.SavedState;
            Resources = resources;
            Reason = reason;
        }

        public ScreenPreparationReason Reason
        {
            get;
        }

        public NavigationEntryId EntryId
        {
            get;
        }
        public RegionInstanceId RegionId
        {
            get;
        }
        public object? SavedState
        {
            get;
        }
        public ResourcePreparationContext Resources
        {
            get;
        }
    }
}
