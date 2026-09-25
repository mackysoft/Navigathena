namespace MackySoft.Navigathena
{
    /// <summary>Overrides the direct history destination's return settings for one Back operation.</summary>
    public sealed record BackOptions
    {
        public ScreenPreparationMode? Preparation
        {
            get; init;
        }
        public ScreenEnterAnimationMode? EnterAnimation
        {
            get; init;
        }
        public NavigationTransition? Transition
        {
            get; init;
        }
        public INavigationProgressReceiver? Progress
        {
            get; init;
        }

        internal NavigationOptions ToNavigationOptions ()
        {
            Apply(new ScreenHistoryReturnOptions()).Validate();
            return new NavigationOptions(Progress) { Transition = Transition, Back = this };
        }

        internal ScreenHistoryReturnOptions Apply (ScreenHistoryReturnOptions defaults)
            => defaults with
            {
                Preparation = Preparation ?? defaults.Preparation,
                EnterAnimation = EnterAnimation ?? defaults.EnterAnimation
            };
    }
}
