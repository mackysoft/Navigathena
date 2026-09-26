namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Pairs one logical entry with its derived presentation participation.</summary>
    public sealed record EffectivePresentation (NavigationEntryId EntryId, PresentationParticipation Participation);

}
