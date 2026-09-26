namespace MackySoft.Navigathena.Presentation
{
    /// <summary>Provides the route, correlation identifiers, and source-bound actions for one prepared presentation.</summary>
    public sealed class PresentationContext
    {
        internal PresentationContext (RegionRouteDefinitionKey routeDefinitionKey, RegionInstanceId regionId, NavigationEntryId entryId, PresentationId presentationId, IScreenNavigation navigation, object? savedState)
        {
            RouteDefinitionKey = routeDefinitionKey;
            RegionId = regionId;
            EntryId = entryId;
            PresentationId = presentationId;
            Navigation = navigation;
            SavedState = savedState;
        }

        public RegionRouteDefinitionKey RouteDefinitionKey
        {
            get;
        }
        public RegionInstanceId RegionId
        {
            get;
        }
        public NavigationEntryId EntryId
        {
            get;
        }
        public PresentationId PresentationId
        {
            get;
        }
        public IScreenNavigation Navigation
        {
            get;
        }

        /// <summary> Gets the immutable entry-local restoration value, or null before the first successful capture. </summary>
        public object? SavedState
        {
            get;
        }
    }
}
