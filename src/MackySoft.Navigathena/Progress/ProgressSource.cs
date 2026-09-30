using MackySoft.Navigathena.Runtime.Progress;

namespace MackySoft.Navigathena
{
    /// <summary>The latest composed state of this transition, not a global progress channel.</summary>
    /// <remarks>The final snapshot remains readable after reporting and observation end. Referenced application data must remain immutable.</remarks>
    public sealed class ProgressSource<TState>
    {
        internal ProgressSource (ProgressState<TState> state) => State = state;

        internal ProgressState<TState> State { get; }
        /// <summary>Reads the most recently reduced snapshot. Reading is thread-safe; application data in the snapshot must be immutable.</summary>
        public TState Current => State.Current;
    }
}
