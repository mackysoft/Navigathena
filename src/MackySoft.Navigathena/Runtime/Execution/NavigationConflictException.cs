using System;

namespace MackySoft.Navigathena
{
    internal sealed class NavigationConflictException : InvalidOperationException
    {
        public NavigationConflictException (string message) : base(message)
        {
        }
    }
}
