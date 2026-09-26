using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{

    /// <summary>Requests recovery for a current presentation-loss or host incident.</summary>
    public interface INavigationRecoveryClient
    {
        ValueTask<NavigationResult> RecoverAsync (NavigationIncidentId incidentId, CancellationToken cancellationToken = default);
    }

}
