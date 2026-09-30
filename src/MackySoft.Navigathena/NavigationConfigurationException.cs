using System;

namespace MackySoft.Navigathena
{

    /// <summary>Indicates an invalid navigation definition or bound navigation action.</summary>
    public sealed class NavigationConfigurationException : InvalidOperationException
    {
        public NavigationConfigurationException (string message) : base(message)
        {
        }
    }

}
