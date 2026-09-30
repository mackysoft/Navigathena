using System;

namespace MackySoft.Navigathena.Runtime.Progress
{
    internal sealed class ProgressReduction<TState, T> : IProgressReduction<TState>
    {
        private readonly ProgressInput<T> input;
        private readonly Func<TState, ProgressUpdate<T>, TState> reduce;
        private readonly bool singleWork;

        internal ProgressReduction (ProgressInput<T> input, Func<TState, ProgressUpdate<T>, TState> reduce, bool singleWork)
        {
            this.input = input;
            this.reduce = reduce;
            this.singleWork = singleWork;
        }

        public IDisposable Connect (NavigationProgressSink sink, ProgressState<TState> state)
        {
            NavigationProgressReporter? previous = null;
            return sink.Subscribe(input, (work, update) =>
            {
                if (singleWork && previous is not null && !ReferenceEquals(previous, work) && previous.IsOpen)
                {
                    throw new NavigationConfigurationException("Concurrent progress producers require an explicit reduction: " + input.Name);
                }
                previous = work;
                state.Apply(update, reduce);
            });
        }
    }
}
