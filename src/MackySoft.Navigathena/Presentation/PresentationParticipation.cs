namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Describes derived availability, foreground position, output, and activity eligibility.</summary>
    /// <remarks>Activity eligibility can continue in the background. Foreground availability is published only after activation and native presentation complete.</remarks>
    public sealed record PresentationParticipation (bool Available, bool Foreground, bool OutputPresented, bool ActivityEligible)
    {
        internal static PresentationParticipation Unavailable { get; } = new(false, false, false, false);
    }

}
