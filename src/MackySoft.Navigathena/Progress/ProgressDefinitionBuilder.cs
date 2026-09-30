using System;
using System.Collections.Generic;
using MackySoft.Navigathena.Runtime.Progress;

namespace MackySoft.Navigathena
{
    /// <summary>Composes producer-owned input types into an application-owned display state.</summary>
    public sealed class ProgressDefinitionBuilder<TState>
    {
        private readonly Func<TState> initialState;
        private readonly HashSet<object> inputs = new();
        private readonly List<IProgressReduction<TState>> reductions = new();

        internal ProgressDefinitionBuilder (Func<TState> initialState)
        {
            this.initialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        }

        /// <summary>Registers the single reduction for this input. The reducer must return a snapshot without mutating previously published state.</summary>
        /// <remarks>Use update.WorkId to preserve separate state for concurrent producers. Reducers run serially on the reporting thread and must be pure: do not report progress or perform display or application side effects from a reducer.</remarks>
        public ProgressDefinitionBuilder<TState> Reduce<T> (ProgressInput<T> input, Func<TState, ProgressUpdate<T>, TState> reduce)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (reduce is null)
            {
                throw new ArgumentNullException(nameof(reduce));
            }
            if (!inputs.Add(input))
            {
                throw new NavigationConfigurationException("A progress input has already been registered: " + input.Name);
            }
            reductions.Add(new ProgressReduction<TState, T>(input, reduce, false));
            return this;
        }

        /// <summary>Captures the current reductions. Later builder changes do not change this definition.</summary>
        public ProgressDefinition<TState> Build () => new(initialState, reductions.ToArray());
    }
}
