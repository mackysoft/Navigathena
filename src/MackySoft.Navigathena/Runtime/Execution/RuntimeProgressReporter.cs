using System;
using System.Threading;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Execution
{

    internal sealed class RuntimeProgressReporter : INavigationOperationProgressReporter
    {
        private readonly NavigationOperationId operationId;
        private readonly INavigationProgressReceiver? receiver;
        private readonly OperationDiagnostics diagnostics;
        private long sequence;

        public RuntimeProgressReporter (NavigationOperationId operationId, INavigationProgressReceiver? receiver, OperationDiagnostics diagnostics)
        {
            this.operationId = operationId;
            this.receiver = receiver;
            this.diagnostics = diagnostics;
        }

        public void Report (NavigationPhase phase, double? phaseFraction)
        {
            if (phaseFraction.HasValue && (double.IsNaN(phaseFraction.Value) || double.IsInfinity(phaseFraction.Value) || phaseFraction.Value < 0d || phaseFraction.Value > 1d))
            {
                diagnostics.Add(phase, "A progress report contained a non-finite or out-of-range phase fraction.", new ArgumentOutOfRangeException(nameof(phaseFraction)));
                return;
            }

            try
            {
                receiver?.Report(new NavigationProgress(operationId, Interlocked.Increment(ref sequence), phase, phaseFraction));
            }
            catch (Exception exception)
            {
                diagnostics.Add(phase, "A navigation progress receiver threw an exception: " + exception.Message, exception);
            }
        }
    }

}
