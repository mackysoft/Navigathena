namespace MackySoft.Navigathena
{
    /// <summary>Refers to one externally owned value without transferring its disposal authority.</summary>
    public sealed class ResourceReference<T> where T : class
    {
        internal ResourceReference (ResourceLifetime lifetime, T value)
        {
            Lifetime = lifetime;
            Value = value;
        }

        internal ResourceLifetime Lifetime
        {
            get;
        }
        internal T Value
        {
            get;
        }
    }
}
