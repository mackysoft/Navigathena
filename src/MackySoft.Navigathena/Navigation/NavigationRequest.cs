using System.Threading;

namespace MackySoft.Navigathena
{

    internal sealed record NavigationRequest (NavigationOperationKind Kind, RegionInstanceId Target, INavigationDestinationTree? Destination, NavigationOptions? Options, CancellationToken CancellationToken, SourcePrecondition? Source)
    {
        internal HistoryTarget? ReplacementTarget
        {
            get; init;
        }
        internal HistoryPrecondition? Replacement
        {
            get; init;
        }
        internal CallChange? CallChange
        {
            get; init;
        }
        public NavigationOperation? Operation
        {
            get; init;
        }
    }

}
