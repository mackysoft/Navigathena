using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Provides navigation and cancellation for exactly one activity period.</summary>
    public class ScreenActivityContext
    {
        internal IScreenCallScope Calls
        {
            get;
        }
        private readonly Func<Func<ScreenWorkContext, ValueTask>, ScreenWork> startWork;
        internal ScreenActivityContext (NavigationEntryId entryId, RegionInstanceId regionId, IScreenNavigation navigation, CancellationToken cancellationToken, IScreenCallScope calls, Func<Func<ScreenWorkContext, ValueTask>, ScreenWork> startWork, bool isFirstActivation, ScreenActivationReason reason, ScreenPreparationReason? preparationReason)
        {
            EntryId = entryId;
            RegionId = regionId;
            Navigation = navigation;
            CancellationToken = cancellationToken;
            Calls = calls;
            this.startWork = startWork;
            IsFirstActivation = isFirstActivation;
            Reason = reason;
            PreparationReason = preparationReason;
        }

        public NavigationEntryId EntryId
        {
            get;
        }
        public RegionInstanceId RegionId
        {
            get;
        }
        public IScreenNavigation Navigation
        {
            get;
        }
        public CancellationToken CancellationToken
        {
            get;
        }
        public ScreenActivationReason Reason
        {
            get;
        }
        public ScreenPreparationReason? PreparationReason
        {
            get;
        }
        /// <summary>True until this history entry has successfully completed its first activation transition.</summary>
        public bool IsFirstActivation
        {
            get;
        }

        /// <summary>Schedules owned work after the current navigation settles. Do not await its completion inside a lifecycle callback.</summary>
        public ScreenWork StartWork (Func<ScreenWorkContext, ValueTask> work) => startWork(work ?? throw new ArgumentNullException(nameof(work)));
    }
}
