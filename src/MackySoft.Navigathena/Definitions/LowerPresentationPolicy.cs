using System;

namespace MackySoft.Navigathena
{

    /// <summary>Defines an entry's effect on lower entries in the same region and their descendant screens.</summary>
    /// <remarks>Effects do not propagate to the owning screen, sibling regions, or the entry's own child screens.</remarks>
    public sealed record LowerPresentationPolicy (LowerPresentationOutput Output, LowerPresentationRetention Retention, LowerPresentationBoundary Boundary)
    {
        /// <summary> Retains lower presentations and their output, with an input boundary when lower history is visible. </summary>
        public static LowerPresentationPolicy Preserve { get; } = new(LowerPresentationOutput.Preserve, LowerPresentationRetention.Retain, LowerPresentationBoundary.WhenLowerVisible);

        /// <summary> Retains lower presentations and their output, and requires an input boundary even without lower history. </summary>
        public static LowerPresentationPolicy Modal { get; } = new(LowerPresentationOutput.Preserve, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required);

        /// <summary> Hides lower output while retaining the presentations. </summary>
        public static LowerPresentationPolicy HideAndRetain { get; } = new(LowerPresentationOutput.Hide, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required);

        /// <summary> Hides lower output and releases presentations without removing their history entries. </summary>
        public static LowerPresentationPolicy HideAndRelease { get; } = new(LowerPresentationOutput.Hide, LowerPresentationRetention.Release, LowerPresentationBoundary.Required);

        internal void Validate ()
        {
            if (!Enum.IsDefined(typeof(LowerPresentationOutput), Output)
                || !Enum.IsDefined(typeof(LowerPresentationRetention), Retention) || !Enum.IsDefined(typeof(LowerPresentationBoundary), Boundary))
            {
                throw new ArgumentException("LowerPresentationPolicy contains an unknown value.");
            }

            if (Retention == LowerPresentationRetention.Release && Output != LowerPresentationOutput.Hide)
            {
                throw new ArgumentException("Releasing lower presentations requires hiding their output.");
            }
        }
    }

}
