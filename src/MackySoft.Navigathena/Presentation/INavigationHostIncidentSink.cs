using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Accepts a host-level incident without allowing caller cancellation to withdraw admission.</summary>
    public interface INavigationHostIncidentSink
    {
        /// <summary>Reports one host incident and waits for its linearized Core result.</summary>
        /// <param name="incident">The immutable incident and required current presentation generations.</param>
        /// <param name="cancellationToken">Cancels only this caller's wait after admission.</param>
        /// <returns>The applied, duplicate, obsolete, or closed report result.</returns>
        ValueTask<HostIncidentReportResult> ReportAsync (NavigationHostIncident incident, CancellationToken cancellationToken = default);
    }

}
