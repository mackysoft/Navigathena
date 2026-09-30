using System;

namespace MackySoft.Navigathena.Runtime.Progress
{
    internal interface IProgressReduction<TState>
    {
        IDisposable Connect (NavigationProgressSink sink, ProgressState<TState> state);
    }
}
