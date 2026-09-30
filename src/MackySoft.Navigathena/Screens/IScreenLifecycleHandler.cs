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
        /// <summary>Initializes the instance once, optionally acquiring instance-owned resources and reporting progress.</summary>
        /// <param name="initialization">Registration and reporting for this invocation. Both close when initialization finishes; registered resources remain owned by the screen instance.</param>
        /// <param name="cancellationToken">Cancellation for initialization work; owned resources are still released if initialization is cancelled.</param>
        ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken);
        /// <summary>Prepares the route's data and saved state before presentation. Does not run again for an ordinary activity-only resume.</summary>
        ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);
        /// <summary>Starts activity after preparation and transition effects finish. Native input opens only after this callback completes.</summary>
        ValueTask ActivateAsync (TRoute route, ScreenActivityContext activity);
        ValueTask DeactivateAsync ();
        /// <summary>Completes final screen-specific shutdown after activity and owned work stop, while screen resources remain available.</summary>
        /// <remarks>Finish operations using initialization or preparation resources here. Those resources may be released before the handler's registered disposal. A failed termination retains its dependencies.</remarks>
        /// <param name="progress">Reports for the ending navigation operation, or an inactive reporter when no operation is running. Retained reports are ignored after termination finishes.</param>
        ValueTask TerminateAsync (NavigationProgressReporter progress);
    }
}
