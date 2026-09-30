using System;
using MackySoft.Navigathena.Runtime.Progress;

namespace MackySoft.Navigathena
{
    /// <summary>Defines either a single producer's state or an application-specific composition of typed inputs.</summary>
    public static class ProgressDefinition
    {
        /// <summary>Starts a composition whose initial state is created separately for each transition execution.</summary>
        public static ProgressDefinitionBuilder<TState> Create<TState> (Func<TState> initialState) => new(initialState);

        /// <summary>Displays one input directly, without an aggregate model or reducer.</summary>
        /// <remarks>Sequential invocations may replace the state. Concurrent reporting invocations require an explicit Reduce definition instead of silently overwriting each other.</remarks>
        public static ProgressDefinition<TState> From<TState> (ProgressInput<TState> input, Func<TState> initialState)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (initialState is null)
            {
                throw new ArgumentNullException(nameof(initialState));
            }
            return new ProgressDefinition<TState>(initialState, new IProgressReduction<TState>[]
            {
                new ProgressReduction<TState, TState>(input, (_, update) => update.Value, true)
            });
        }
    }
}
