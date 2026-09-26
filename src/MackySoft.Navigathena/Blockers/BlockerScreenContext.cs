namespace MackySoft.Navigathena
{
    /// <summary>Identifies the screen using a blocker, not a lower screen being blocked.</summary>
    public sealed class BlockerScreenContext
    {
        internal BlockerScreenContext (NavigationEntry entry, IScreenNavigation navigation)
        {
            EntryId = entry.Id;
            RegionId = entry.RegionId;
            Route = entry.Route;
            Navigation = navigation;
        }
        public RegionInstanceId RegionId
        {
            get;
        }
        public NavigationEntryId EntryId
        {
            get;
        }
        public NavigationRoute Route
        {
            get;
        }
        public IScreenNavigation Navigation
        {
            get;
        }
    }
}
