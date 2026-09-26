namespace MackySoft.Navigathena
{

    /// <summary>Configures construction, transition selection and observation for one navigation operation.</summary>
    public sealed record NavigationOptions (INavigationProgressReceiver? Progress = null)
    {
        /// <summary>Builds fresh destination instances and initial children rather than reusing existing instances. External borrowed objects remain externally owned.</summary>
        public bool RecreateInstance
        {
            get; init;
        }
        internal BackOptions? Back
        {
            get; init;
        }
        internal ReloadOptions? Reload
        {
            get; init;
        }
        /// <summary>Overrides the configured transition; null delegates to policy and None explicitly omits the whole-transition effect.</summary>
        public NavigationTransition? Transition
        {
            get; init;
        }
    }

}
