using System;

namespace MackySoft.Navigathena.Runtime.Planning
{
    internal sealed class NavigationRejectionException : Exception
    {
        public NavigationRejectionException (string message) : base(message)
        {
        }
    }
}
