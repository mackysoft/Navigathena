using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary> Provides navigation and cancellation for one foreground activation period. </summary>
    /// <remarks> The period ends on departure or modal cover. A retained return receives a new context. Rendering, game simulation and binding-owned work are not owned by this period. </remarks>
    public class ScreenActivityContext
    {
        internal IScreenCallScope Calls { get; }
        private readonly Func<Func<ScreenWorkContext, ValueTask>, ScreenWork> startWork;
        internal ScreenActivityContext (NavigationEntryId entryId, RegionInstanceId regionId, IScreenNavigation navigation, CancellationToken cancellationToken, ScreenForegroundToken foregroundToken, IScreenCallScope calls, Func<Func<ScreenWorkContext, ValueTask>, ScreenWork> startWork, bool isFirstActivation, ScreenActivationReason reason, ScreenPreparationReason? preparationReason)
        {
            EntryId = entryId;
            RegionId = regionId;
            Navigation = navigation;
            CancellationToken = cancellationToken;
            ForegroundToken = foregroundToken;
            Calls = calls;
            this.startWork = startWork;
            IsFirstActivation = isFirstActivation;
            Reason = reason;
            PreparationReason = preparationReason;
        }

        public NavigationEntryId EntryId { get; }
        public RegionInstanceId RegionId { get; }
        public IScreenNavigation Navigation { get; }
        /// <summary> Signals the end of this activation period. Use a separate lifetime for work that must survive modal cover. </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary> The valid operation qualification for this activation. Native input and focus admission are ready when activation starts. </summary>
        /// <remarks> Retain this value when accepting an operation. Departure invalidates it before native focus-loss callbacks; a later activation supplies a different token. </remarks>
        public ScreenForegroundToken ForegroundToken { get; }
        public ScreenActivationReason Reason { get; }
        public ScreenPreparationReason? PreparationReason { get; }
        /// <summary>True until this history entry has successfully completed its first activation transition.</summary>
        public bool IsFirstActivation { get; }

        /// <summary>Schedules owned work after the current navigation settles. Do not await its completion inside a lifecycle callback.</summary>
        /// <remarks>Work registered during activation starts only after activation succeeds; a failed activation cancels its pending work. Once started, work survives deactivation until it finishes, is canceled, or its screen binding ends.</remarks>
        /// <param name="work">The complete asynchronous operation, including any cleanup that uses screen resources.</param>
        /// <returns>A handle for observing completion or requesting execution cancellation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="work"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This activity or its screen binding has ended.</exception>
        public ScreenWork StartWork (Func<ScreenWorkContext, ValueTask> work) => startWork(work ?? throw new ArgumentNullException(nameof(work)));
    }
}
