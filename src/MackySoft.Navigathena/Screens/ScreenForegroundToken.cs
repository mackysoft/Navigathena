using System.Threading;

namespace MackySoft.Navigathena
{
    /// <summary> Represents the eligibility of a screen for application operations during one foreground period. </summary>
    /// <remarks>
    /// The runtime supplies this token through ScreenActivityContext after native input admission, when activation starts.
    /// The application can retain it and check IsValid before executing an operation. Returning to the foreground issues
    /// a new token; an invalid token never becomes valid again. Copies refer to the same foreground period.
    /// The default value is invalid.
    /// </remarks>
    public readonly struct ScreenForegroundToken
    {
        private readonly ScreenForegroundPeriod? period;

        internal ScreenForegroundToken (NavigationEntryId entryId, RegionInstanceId regionId, ScreenForegroundPeriod period)
        {
            EntryId = entryId;
            RegionId = regionId;
            this.period = period;
        }

        public NavigationEntryId EntryId { get; }
        public RegionInstanceId RegionId { get; }

        /// <summary> Is true while the token's foreground period remains available for application operations. </summary>
        public bool IsValid => period?.IsValid == true;

        /// <summary> Signals revocation after native input closes; use it only for work that must end with this foreground period. </summary>
        /// <remarks> Eligibility is invalidated before native focus-loss callbacks or cancellation notification. The default value returns CancellationToken.None. Work that must survive a popup needs a separate lifetime. </remarks>
        public CancellationToken CancellationToken => period?.CancellationToken ?? CancellationToken.None;
    }
}
