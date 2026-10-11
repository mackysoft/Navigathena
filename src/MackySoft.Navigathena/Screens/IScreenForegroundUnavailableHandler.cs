namespace MackySoft.Navigathena
{
    /// <summary> Optionally observes the end of foreground periods received by this screen's lifecycle participant. </summary>
    /// <remarks> Implement this interface when invalidation of the foreground token alone does not finish application-specific work. </remarks>
    public interface IScreenForegroundUnavailableHandler : IScreenForegroundLifecycleHandler
    {
        /// <summary> Runs after native admission closes and the previous foreground token becomes invalid, before deactivation or termination. </summary>
        /// <remarks>
        /// Also runs on transition suspension, presentation loss, and shutdown. This notification ends a previously supplied
        /// foreground period; it does not ask the application to block input or clear selection in the closed screen.
        /// </remarks>
        void OnForegroundUnavailable ();
    }
}
