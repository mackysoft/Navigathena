using System;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed class NavigationConflictException : InvalidOperationException
    {
        public NavigationConflictException (string message) : base(message)
        {
        }
    }
}
