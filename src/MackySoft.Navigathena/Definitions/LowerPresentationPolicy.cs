using System;

namespace MackySoft.Navigathena
{

    /// <summary>Defines an entry's effect on lower entries in the same region and their descendant screens.</summary>
    /// <remarks>Effects do not propagate to the owning screen, sibling regions, or the entry's own child screens.</remarks>
    public sealed record LowerPresentationPolicy (LowerPresentationOutput Output, LowerPresentationInput Input, LowerPresentationRetention Retention)
    {
        /// <summary>Leaves lower output and input unchanged and retains the presentations.</summary>
        public static LowerPresentationPolicy Preserve { get; } = new(LowerPresentationOutput.Preserve, LowerPresentationInput.PassThrough, LowerPresentationRetention.Retain);
        /// <summary>Blocks lower input while leaving output unchanged and retaining the presentations.</summary>
        public static LowerPresentationPolicy BlockInput { get; } = new(LowerPresentationOutput.Preserve, LowerPresentationInput.Block, LowerPresentationRetention.Retain);
        /// <summary>Hides lower output and blocks input while retaining the presentations.</summary>
        public static LowerPresentationPolicy HideAndRetain { get; } = new(LowerPresentationOutput.Hide, LowerPresentationInput.Block, LowerPresentationRetention.Retain);
        /// <summary>Hides lower output, blocks input, and releases the presentations without removing their history entries.</summary>
        public static LowerPresentationPolicy HideAndRelease { get; } = new(LowerPresentationOutput.Hide, LowerPresentationInput.Block, LowerPresentationRetention.Release);

        internal void Validate ()
        {
            if (!Enum.IsDefined(typeof(LowerPresentationOutput), Output) || !Enum.IsDefined(typeof(LowerPresentationInput), Input) || !Enum.IsDefined(typeof(LowerPresentationRetention), Retention))
            {
                throw new ArgumentException("LowerPresentationPolicy contains an unknown value.");
            }

            if (Retention == LowerPresentationRetention.Release && (Output != LowerPresentationOutput.Hide || Input != LowerPresentationInput.Block))
            {
                throw new ArgumentException("Releasing lower presentations requires hiding their output and blocking their input.");
            }
        }
    }

}
