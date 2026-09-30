using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Navigation
{
    internal static class NavigationCallbackScope
    {
        private static readonly AsyncLocal<Invocation?> Executing = new();
        public static bool IsExecuting => Executing.Value?.active == true;

        public static async ValueTask RunAsync (Func<ValueTask> callback)
        {
            Invocation? previous = Executing.Value;
            Invocation invocation = new();
            Executing.Value = invocation;
            try
            {
                await callback();
            }
            finally
            {
                invocation.active = false;
                Executing.Value = previous;
            }
        }

        private sealed class Invocation
        {
            public volatile bool active = true;
        }
    }
}
