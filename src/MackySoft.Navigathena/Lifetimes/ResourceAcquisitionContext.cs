using System;

namespace MackySoft.Navigathena
{
    /// <summary>Reports loss of the specific resource generation being acquired.</summary>
    public sealed class ResourceAcquisitionContext
    {
        private readonly Action<string> reportLoss;

        internal ResourceAcquisitionContext (Action<string> reportLoss) => this.reportLoss = reportLoss;

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
