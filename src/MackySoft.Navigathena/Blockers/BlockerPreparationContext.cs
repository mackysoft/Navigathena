namespace MackySoft.Navigathena
{
    /// <summary>Registration available only during the runtime-owned preparation callback.</summary>
    public abstract class BlockerPreparationContext
    {
        internal BlockerPreparationContext ()
        {
        }
        /// <summary>Ownership and borrowing for this blocker instance.</summary>
        public abstract LifetimeContext Lifetime
        {
            get;
        }
        public abstract void RegisterViewAdapter (IViewAdapter adapter);
    }
}
