using System.Threading;

namespace MackySoft.Navigathena
{
    /// <summary> Represents the eligibility of a screen for application operations during one foreground period. </summary>
    /// <remarks>
    /// The runtime issues this token when foreground presentation is ready. The application can retain it and check
    /// IsValid before executing an operation. Activity may outlive this period. Returning to the foreground issues
    /// a new token; an invalid token never becomes valid again. Copies refer to the same foreground period.
    /// The default value is invalid.
    /// </remarks>
    public readonly struct ScreenForegroundToken
    {
        internal ScreenForegroundToken (NavigationEntryId entryId, RegionInstanceId regionId, CancellationToken cancellationToken)
        {
            EntryId = entryId;
            RegionId = regionId;
            CancellationToken = cancellationToken;
        }

        public NavigationEntryId EntryId { get; }
        public RegionInstanceId RegionId { get; }

        /// <summary> Is true while the token's foreground period remains available for application operations. </summary>
        public bool IsValid => CancellationToken.CanBeCanceled && !CancellationToken.IsCancellationRequested;

        /// <summary> Signals the end of an issued foreground period, independently of activity; pass it to cancellable asynchronous work. </summary>
        /// <remarks> The default value returns CancellationToken.None. Check IsValid before starting an application operation. </remarks>
        public CancellationToken CancellationToken { get; }
    }
}
