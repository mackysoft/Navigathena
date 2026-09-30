using System;

namespace MackySoft.Navigathena
{
    /// <summary>Reports loss of the specific resource generation being acquired.</summary>
    public sealed class ResourceAcquisitionContext
    {
        private readonly Action<string> reportLoss;

        internal ResourceAcquisitionContext (Action<string> reportLoss, NavigationProgressReporter progress)
        {
            this.reportLoss = reportLoss;
            Progress = progress;
        }

        /// <summary>Reports acquisition progress until AcquireAsync returns or throws. It is not a release-progress channel.</summary>
        public NavigationProgressReporter Progress { get; }

        public void ReportLoss (string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A loss reason is required.", nameof(reason));
            }

            reportLoss(reason);
        }
    }
}
