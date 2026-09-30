using System;

namespace MackySoft.Navigathena
{
    /// <summary>Registration available only during the runtime-owned preparation callback.</summary>
    public abstract class NavigationTransitionPreparationContext
    {
        internal NavigationTransitionPreparationContext ()
        {
        }
        /// <summary>Ownership and borrowing for this transition effect.</summary>
        public abstract LifetimeContext Lifetime { get; }
        public abstract void RegisterViewAdapter (IViewAdapter adapter);
        public abstract void RegisterExistingViewAdapter (IViewAdapter adapter);

        /// <summary>Immediately receives the current state, then ordered updates until this effect settles or fails.</summary>
        /// <remarks>The runtime owns unsubscription and waits for an in-flight callback before releasing the display. Callbacks run synchronously on the reporting thread; use a UI adapter when reports originate off the UI thread. Observer failures are reported by the navigation operation after safe settlement.</remarks>
        /// <exception cref="NavigationConfigurationException">The source belongs to another navigation operation.</exception>
        public abstract void ObserveProgress<TState> (ProgressSource<TState> source, Action<TState> receive);

        internal abstract ProgressSource<TState> CreateProgressSource<TState> (ProgressDefinition<TState> definition);
    }
}
