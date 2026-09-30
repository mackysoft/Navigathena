namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Reports best-effort progress for the current navigation operation.</summary>
    public interface INavigationOperationProgressReporter
    {
        void Report (NavigationPhase phase, double? phaseFraction);
        /// <summary>Creates an invocation-scoped producer. The caller closes its reporting interval after the work finishes.</summary>
        NavigationProgressReporter CreateWork (NavigationPhase phase, NavigationEntryId? entryId, string name);
    }

}
