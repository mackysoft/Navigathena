namespace MackySoft.Navigathena
{
    /// <summary>Registration available only during the runtime-owned preparation callback.</summary>
    public abstract class BlockerPreparationContext
    {
        internal BlockerPreparationContext ()
        {
        }
        public abstract ResourcePreparationContext Resources
        {
            get;
        }
        public abstract void RegisterViewAdapter (IViewAdapter adapter);
    }
}
