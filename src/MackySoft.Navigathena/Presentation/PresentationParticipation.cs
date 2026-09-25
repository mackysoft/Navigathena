namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Describes an entry's derived availability, output, and semantic input participation.</summary>
    public sealed record PresentationParticipation (bool Available, bool Foreground, bool OutputPresented, bool SemanticInputEligible)
    {
        internal static PresentationParticipation Unavailable { get; } = new(false, false, false, false);
    }

}
