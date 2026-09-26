using System;

namespace MackySoft.Navigathena
{
    /// <summary>Controls preparation and screen enter animation, never physical instance lifetime.</summary>
    public sealed record ScreenHistoryReturnOptions
    {
        public ScreenPreparationMode Preparation { get; init; } = ScreenPreparationMode.WhenRequired;
        public ScreenEnterAnimationMode EnterAnimation { get; init; } = ScreenEnterAnimationMode.WhenChanged;

        internal void Validate ()
        {
            if (!Enum.IsDefined(typeof(ScreenPreparationMode), Preparation)
                || !Enum.IsDefined(typeof(ScreenEnterAnimationMode), EnterAnimation))
            {
                throw new NavigationConfigurationException("Unknown history return preparation or animation mode.");
            }
        }
    }
}
