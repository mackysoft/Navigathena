using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Implements awaited screen lifecycle work. The runtime controls invocation; the registered owner controls disposal.</summary>
    /// <remarks>
    /// The runtime checks the supplied token immediately before and after InitializeAsync, PrepareAsync and ActivateAsync
    /// (using the activity token for activation). Implementations need not repeat the entry check, but must forward the token
    /// to asynchronous work and cooperate with cancellation during execution. Cancellation cannot atomically prevent entry
    /// or forcibly stop work. DeactivateAsync and TerminateAsync are awaited even after cancellation; resources remain owned until stopping completes.
    /// </remarks>
    public interface IScreenLifecycleHandler<in TRoute> where TRoute : Route
    {
        ValueTask InitializeAsync (CancellationToken cancellationToken);
        ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);
        ValueTask ActivateAsync (TRoute route, ScreenActivityContext activity);
        ValueTask DeactivateAsync ();
        ValueTask TerminateAsync ();
    }
}
