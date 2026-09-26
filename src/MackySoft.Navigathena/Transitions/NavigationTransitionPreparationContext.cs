namespace MackySoft.Navigathena
{
    /// <summary>Registration available only during the runtime-owned preparation callback.</summary>
    public abstract class NavigationTransitionPreparationContext
    {
        internal NavigationTransitionPreparationContext ()
        {
        }
        /// <summary>Ownership and borrowing for this transition effect.</summary>
        public abstract LifetimeContext Lifetime
        {
            get;
        }
        public abstract void RegisterViewAdapter (IViewAdapter adapter);
        public abstract void RegisterExistingViewAdapter (IViewAdapter adapter);
    }
}
