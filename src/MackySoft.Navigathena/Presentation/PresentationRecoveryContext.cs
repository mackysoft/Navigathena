namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Correlates a recovery transition with its exact incident kind and identifier.</summary>
    public sealed record PresentationRecoveryContext (PresentationRecoveryKind Kind, NavigationIncidentId IncidentId);

}
