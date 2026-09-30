namespace MackySoft.Navigathena
{
    /// <summary>Provides instance-owned acquisition and progress during a screen's one-time initialization.</summary>
    /// <remarks>Registration and reporting close when initialization finishes. Acquired resources remain owned until the screen ends.</remarks>
    public sealed class ScreenInitializationContext
    {
        internal ScreenInitializationContext (LifetimeContext lifetime, NavigationProgressReporter progress)
        {
            Lifetime = lifetime;
            Progress = progress;
        }

        /// <summary>Registers resources in the same ownership sequence as construction, without configuring presentation or dependency injection.</summary>
        /// <remarks>These later registrations release before earlier construction registrations. Use TerminateAsync for final operations that need them.</remarks>
        public LifetimeContext Lifetime { get; }

        /// <summary>Reports typed values or fractions for this initialization invocation.</summary>
        public NavigationProgressReporter Progress { get; }
    }
}
