namespace MackySoft.Navigathena
{
    /// <summary>Input-specific preparation and lifetime for one displayed history entry; never a DI registration context.</summary>
    public sealed class ScreenPreparationContext
    {
        internal ScreenPreparationContext (NavigationEntry entry, LifetimeContext lifetime, ScreenPreparationReason reason)
        {
            EntryId = entry.Id;
            RegionId = entry.RegionId;
            SavedState = entry.SavedState;
            Lifetime = lifetime;
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
        /// <summary>Ownership and borrowing for this preparation. Previous preparations are released once they are no longer in use.</summary>
        public LifetimeContext Lifetime
        {
            get;
        }
    }
}
