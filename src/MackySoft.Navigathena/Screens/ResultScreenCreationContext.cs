namespace MackySoft.Navigathena
{
    /// <summary>Construction capabilities for a screen that must return an answer.</summary>
    public sealed class ScreenCreationContext<TRoute, TResult> : ScreenCreationContext<TRoute> where TRoute : Route<TResult>
    {
        internal ScreenCreationContext (ScreenCreationServices creation) : base(creation)
        {
        }
    }
}
