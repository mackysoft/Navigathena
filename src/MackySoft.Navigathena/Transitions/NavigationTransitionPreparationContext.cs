namespace MackySoft.Navigathena
{
    /// <summary>Registration available only during the runtime-owned preparation callback.</summary>
    public abstract class NavigationTransitionPreparationContext
    {
        internal NavigationTransitionPreparationContext ()
        {
        }
        public abstract ResourcePreparationContext Resources
        {
            get;
        }
        public abstract void RegisterViewAdapter (IViewAdapter adapter);
        public abstract void RegisterExistingViewAdapter (IViewAdapter adapter);
    }
}
