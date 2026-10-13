namespace MackySoft.Navigathena.Presentation
{

    /// <summary> Describes derived availability, foreground position, and output. </summary>
    /// <remarks> A screen is eligible for activation only while it is available, in the foreground, and presented. Eligibility does not indicate that preparation or native input admission has finished. </remarks>
    public sealed record PresentationParticipation (bool Available, bool Foreground, bool OutputPresented)
    {
        public bool ActivityEligible => Available && Foreground && OutputPresented;

        internal static PresentationParticipation Unavailable { get; } = new(false, false, false);
    }

}
