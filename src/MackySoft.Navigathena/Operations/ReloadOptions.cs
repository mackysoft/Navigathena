namespace MackySoft.Navigathena
{
    /// <summary>Recreates the current screen and its child instances without adding or removing history entries.</summary>
    public sealed record ReloadOptions
    {
        /// <summary>Restores captured display state. The default prepares the same route afresh.</summary>
        public bool RestoreState
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
            => new(Progress)
            {
                Transition = Transition,
                Reload = this
            };
    }
}
