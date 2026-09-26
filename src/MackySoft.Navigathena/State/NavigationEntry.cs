namespace MackySoft.Navigathena
{

    /// <summary>Stores the immutable route and registration selected for one history entry.</summary>
    public sealed record NavigationEntry (NavigationEntryId Id, RegionInstanceId RegionId, NavigationRoute Route, RegionRouteDefinitionKey RouteDefinitionKey)
    {
        internal System.Guid? CallId
        {
            get; init;
        }
        internal NavigationEntryId? CallOwnerEntryId
        {
            get; init;
        }
        /// <summary>The construction definition selected for this history entry, independent of its physical realization.</summary>
        public System.Guid? ScreenDefinitionId
        {
            get; internal init;
        }
        /// <summary> Gets the last successfully captured restoration value for this entry, independently of its route and physical screen. </summary>
        /// <remarks> The adapter supplies an immutable value without references to physical resources. This value is not game progress or persistent storage. </remarks>
        public object? SavedState
        {
            get; internal init;
        }
    }

}
