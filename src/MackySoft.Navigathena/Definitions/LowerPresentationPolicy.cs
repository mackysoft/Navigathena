using System;

namespace MackySoft.Navigathena
{

    /// <summary>Defines an entry's effect on lower entries in the same region and their descendant screens.</summary>
    /// <remarks>Effects do not propagate to the owning screen, sibling regions, or the entry's own child screens.</remarks>
    public sealed record LowerPresentationPolicy (LowerPresentationOutput Output, LowerPresentationActivity Activity, LowerPresentationRetention Retention, LowerPresentationBoundary Boundary)
    {
        /// <summary>Retains lower presentations with their output and activity. Background native input remains closed.</summary>
        public static LowerPresentationPolicy Preserve { get; } = new(LowerPresentationOutput.Preserve, LowerPresentationActivity.Continue, LowerPresentationRetention.Retain, LowerPresentationBoundary.WhenLowerVisible);
        /// <summary>Ends lower activity periods while retaining their presentations and output.</summary>
        public static LowerPresentationPolicy SuspendActivity { get; } = new(LowerPresentationOutput.Preserve, LowerPresentationActivity.Suspend, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required);
        /// <summary>Hides lower output and ends activity periods while retaining the presentations.</summary>
        public static LowerPresentationPolicy HideAndRetain { get; } = new(LowerPresentationOutput.Hide, LowerPresentationActivity.Suspend, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required);
        /// <summary>Hides lower output, ends activity periods, and releases presentations without removing their history entries.</summary>
        public static LowerPresentationPolicy HideAndRelease { get; } = new(LowerPresentationOutput.Hide, LowerPresentationActivity.Suspend, LowerPresentationRetention.Release, LowerPresentationBoundary.Required);

        internal void Validate ()
        {
            if (!Enum.IsDefined(typeof(LowerPresentationOutput), Output) || !Enum.IsDefined(typeof(LowerPresentationActivity), Activity)
                || !Enum.IsDefined(typeof(LowerPresentationRetention), Retention) || !Enum.IsDefined(typeof(LowerPresentationBoundary), Boundary))
            {
                throw new ArgumentException("LowerPresentationPolicy contains an unknown value.");
            }

            if (Retention == LowerPresentationRetention.Release && (Output != LowerPresentationOutput.Hide || Activity != LowerPresentationActivity.Suspend))
            {
                throw new ArgumentException("Releasing lower presentations requires hiding their output and suspending their activity.");
            }
        }
    }

}
