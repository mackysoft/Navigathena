namespace MackySoft.Navigathena
{

    /// <summary>Provides a phase-specific reason suitable for recovery and retry decisions.</summary>
    public sealed record NavigationDiagnostic (NavigationPhase Phase, string Reason, NavigationEntryId? EntryId = null, PresentationId? PresentationId = null)
    {
        public System.Exception? Exception
        {
            get; init;
        }
    }

}
