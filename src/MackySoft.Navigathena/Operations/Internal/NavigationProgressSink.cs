using System;

namespace MackySoft.Navigathena
{
    internal abstract class NavigationProgressSink
    {
        public object SyncRoot { get; } = new();
        public abstract void Report<T> (NavigationProgressReporter work, ProgressInput<T> input, T value);
        public abstract IDisposable Subscribe<T> (ProgressInput<T> input, Action<NavigationProgressReporter, ProgressUpdate<T>> receive);
        public abstract void ReportFailure (NavigationPhase phase, Exception exception);
    }
}
