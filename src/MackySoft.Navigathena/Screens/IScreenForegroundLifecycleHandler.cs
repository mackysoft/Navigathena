namespace MackySoft.Navigathena
{
    /// <summary>Optionally observes foreground availability on the screen's lifecycle participant.</summary>
    /// <remarks>
    /// Foreground periods are independent of activity periods: a retained screen can keep activity while covered.
    /// The application chooses its initial or returning focus target in OnForegroundAvailable using its UI framework.
    /// Callbacks are synchronous lifecycle notifications; use posted navigation rather than requesting navigation inside them.
    /// </remarks>
    public interface IScreenForegroundLifecycleHandler
    {
        /// <summary>Runs after transition settlement and successful publication of view permissions, blockers, and ordering.</summary>
        /// <param name="foreground">A fresh foreground period. It never becomes valid again after cancellation.</param>
        void OnForegroundAvailable (ScreenForegroundContext foreground);

        /// <summary>Runs after native admission closes and the previous foreground period is cancelled, before deactivation or termination.</summary>
        /// <remarks>Also runs when a transition temporarily suspends foreground availability, on presentation loss, and during shutdown.</remarks>
        void OnForegroundUnavailable ();
    }
}
