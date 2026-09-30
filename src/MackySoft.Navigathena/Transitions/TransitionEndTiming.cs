namespace MackySoft.Navigathena
{
    /// <summary>Determines which work the whole-transition effect covers before it settles.</summary>
    public enum TransitionEndTiming
    {
        /// <summary>Settle after presentation animations, before releasing retired screen resources.</summary>
        AfterPresentation,

        /// <summary>Keep the effect connected until retired screen and preparation resources have finished releasing.</summary>
        /// <remarks>Resources still used by retained calls or screen work are not yet eligible for release and do not delay settlement.</remarks>
        AfterResourceRelease,
    }
}
