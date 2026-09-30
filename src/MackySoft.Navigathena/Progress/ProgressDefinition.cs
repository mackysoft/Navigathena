using System;
using MackySoft.Navigathena.Runtime.Progress;

namespace MackySoft.Navigathena
{
    /// <summary>Creates an independent progress state for each transition using this definition.</summary>
    public sealed class ProgressDefinition<TState>
    {
        private readonly Func<TState> initialState;
        private readonly IProgressReduction<TState>[] reductions;

        internal ProgressDefinition (Func<TState> initialState, IProgressReduction<TState>[] reductions)
        {
            this.initialState = initialState;
            this.reductions = reductions;
        }

        internal ProgressSource<TState> CreateSource (NavigationProgressSink sink)
        {
            ProgressState<TState> state = new(sink, initialState());
            try
            {
                foreach (IProgressReduction<TState> reduction in reductions)
                {
                    state.AddInput(reduction.Connect(sink, state));
                }
                return new ProgressSource<TState>(state);
            }
            catch
            {
                state.Dispose();
                throw;
            }
        }
    }
}
