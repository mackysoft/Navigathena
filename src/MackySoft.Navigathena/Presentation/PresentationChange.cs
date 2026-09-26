namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Describes an entry's presentation-state and derived-participation change.</summary>
    public sealed record PresentationChange (NavigationEntry? BeforeEntry, NavigationEntry? AfterEntry, PresentationState? Before, PresentationState? After, PresentationParticipation? BeforeParticipation, PresentationParticipation? AfterParticipation, PresentationContext? Context);

}
