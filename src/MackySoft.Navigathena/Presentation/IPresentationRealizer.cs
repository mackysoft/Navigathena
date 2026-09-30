namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Begins the adapter-owned transaction that realizes one planned Core transition.</summary>
    internal interface IPresentationRealizer
    {
        IPresentationTransaction Begin (PresentationTransition transition, INavigationOperationProgressReporter progress);
    }

}
