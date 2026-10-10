using System.Threading;

namespace MackySoft.Navigathena
{
    /// <summary>Identifies one period in which a screen's foreground presentation is ready for application use.</summary>
    /// <remarks>Activity may outlive this period. Reopening a screen supplies a new context rather than reviving an old one.</remarks>
    public sealed class ScreenForegroundContext
    {
        internal ScreenForegroundContext (NavigationEntryId entryId, RegionInstanceId regionId, CancellationToken cancellationToken)
        {
            EntryId = entryId;
            RegionId = regionId;
            CancellationToken = cancellationToken;
        }

        public NavigationEntryId EntryId { get; }
        public RegionInstanceId RegionId { get; }

        /// <summary>Remains valid until the screen leaves its current foreground-availability period.</summary>
        public bool IsValid => !CancellationToken.IsCancellationRequested;

        /// <summary>Is cancelled when foreground availability ends, independently of the activity token.</summary>
        public CancellationToken CancellationToken { get; }
    }
}
