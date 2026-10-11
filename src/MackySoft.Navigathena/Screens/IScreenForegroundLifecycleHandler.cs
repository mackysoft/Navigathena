namespace MackySoft.Navigathena
{
    /// <summary>Optionally observes foreground availability on the screen's lifecycle participant.</summary>
    /// <remarks>
    /// Foreground periods are independent of activity periods: a retained screen can keep activity while covered.
    /// The application chooses its initial or returning focus target in OnForegroundAvailable using its UI framework.
    /// Implement IScreenForegroundUnavailableHandler only when application work also needs an end notification.
    /// Callbacks are synchronous lifecycle notifications; use posted navigation rather than requesting navigation inside them.
    /// </remarks>
    public interface IScreenForegroundLifecycleHandler
    {
        /// <summary>Runs after transition settlement and successful publication of view permissions, blockers, and ordering.</summary>
        /// <param name="foreground"> A new foreground eligibility token that the application may retain. Once invalid, it never becomes valid again. </param>
        void OnForegroundAvailable (ScreenForegroundToken foreground);
    }
}
