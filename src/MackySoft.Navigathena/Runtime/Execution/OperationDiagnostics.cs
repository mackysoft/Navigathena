using System.Collections.Generic;

namespace MackySoft.Navigathena.Runtime.Execution
{

    internal sealed class OperationDiagnostics
    {
        private readonly object sync = new();
        private readonly List<NavigationDiagnostic> items = new();

        public void Add (NavigationPhase phase, string reason, System.Exception? exception = null)
        {
            lock (sync)
            {
                items.Add(new NavigationDiagnostic(phase, reason) { Exception = exception });
            }
        }

        public IReadOnlyList<NavigationDiagnostic> Snapshot ()
        {
            lock (sync)
            {
                return items.ToArray();
            }
        }
    }

}
