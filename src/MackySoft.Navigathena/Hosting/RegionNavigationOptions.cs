namespace MackySoft.Navigathena
{
    /// <summary>Configures presentation choices independently of screen construction.</summary>
    public sealed class RegionNavigationOptions
    {
        public NavigationTransition? DefaultTransition
        {
            get; init;
        }
        public NavigationTransitionRules Transitions { get; } = new();
        public ScreenResourcePolicy ResourcePolicy
        {
            get; init;
        }
    }
}
