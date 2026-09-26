namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Reports best-effort progress for the current navigation operation.</summary>
    public interface INavigationOperationProgressReporter
    {
        void Report (NavigationPhase phase, double? phaseFraction);
    }

}
