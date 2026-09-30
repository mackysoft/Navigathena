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

        /// <summary>Registers resources owned by this screen instance, without configuring its presentation or dependency injection.</summary>
        public LifetimeContext Lifetime { get; }

        /// <summary>Reports typed values or fractions for this initialization invocation.</summary>
        public NavigationProgressReporter Progress { get; }
    }
}
