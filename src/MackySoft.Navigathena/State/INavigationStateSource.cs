using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{

    /// <summary>Provides immutable state snapshots and awaits states newer than an observed revision.</summary>
    public interface INavigationStateSource
    {
        NavigationState Current
        {
            get;
        }
        ValueTask<NavigationState> WaitForChangeAsync (long observedRevision, CancellationToken cancellationToken = default);
    }

}
