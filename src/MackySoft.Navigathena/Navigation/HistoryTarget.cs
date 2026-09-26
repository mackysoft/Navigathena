using System;

namespace MackySoft.Navigathena
{
    /// <summary>Selects the inclusive start of a replacement within one region's history.</summary>
    public sealed class HistoryTarget
    {
        private HistoryTarget (NavigationEntryId? entryId = null, Type? routeType = null)
        {
            EntryId = entryId;
            RouteType = routeType;
        }

        /// <summary>Selects the current visit. Screen navigation retains its self-replacement guard.</summary>
        public static HistoryTarget Current { get; } = new();

        /// <summary>Selects exactly one visit of the specified concrete route type. Missing or ambiguous matches are rejected.</summary>
        public static HistoryTarget Unique<TRoute> () where TRoute : NavigationRoute => new(routeType: typeof(TRoute));

        /// <summary>Selects an existing visit in the target region, not a physical screen instance.</summary>
        public static HistoryTarget Entry (NavigationEntryId entryId) => new(entryId);

        internal NavigationEntryId? EntryId
        {
            get;
        }
        internal Type? RouteType
        {
            get;
        }
        internal bool IsCurrent => EntryId is null && RouteType is null;
    }
}
