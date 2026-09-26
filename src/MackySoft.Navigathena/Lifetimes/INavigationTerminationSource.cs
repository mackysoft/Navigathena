using System.Threading;
using System.Threading.Tasks;
namespace MackySoft.Navigathena
{
    public interface INavigationTerminationSource
    {
        NavigationTerminationSnapshot Current
        {
            get;
        }
        ValueTask<NavigationTerminationSnapshot> WaitForChangeAsync (long observedRevision, CancellationToken waitCancellationToken = default);
    }
}
