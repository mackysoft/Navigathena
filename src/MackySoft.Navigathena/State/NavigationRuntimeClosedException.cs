using System;

namespace MackySoft.Navigathena
{

    /// <summary>Indicates that an operation or state wait cannot continue because its navigation runtime closed.</summary>
    public sealed class NavigationRuntimeClosedException : InvalidOperationException
    {
        public NavigationRuntimeClosedException () : base("The navigation runtime is closed.")
        {
        }
    }

}
