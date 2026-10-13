using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary> Implements awaited instance initialization, content preparation, foreground entry and departure, and final termination. </summary>
    /// <remarks>
    /// The runtime checks the supplied token immediately before and after InitializeAsync, PrepareAsync and ActivateAsync
    /// (using the activity token for activation). Implementations need not repeat the entry check, but must forward the token
    /// to asynchronous work and cooperate with cancellation during execution. Cancellation cannot atomically prevent entry
    /// or forcibly stop work. DeactivateAsync and TerminateAsync are awaited even after cancellation; resources remain owned until stopping completes.
    /// Each foreground departure ends the activation period, including modal cover. Retained output, game simulation,
    /// data updates and binding-owned work have independent lifetimes; deactivation does not hide or destroy the screen.
    /// Native input and focus admission are ready when ActivateAsync starts. Choose the focus target and establish
    /// operation subscriptions there, retaining the supplied foreground token when qualification is needed.
    /// </remarks>
    public interface IScreenLifecycleHandler<in TRoute> where TRoute : Route
    {
        /// <summary> Initializes the instance once, optionally acquiring instance-owned resources and reporting progress. </summary>
        /// <param name="initialization">Registration and reporting for this invocation. Both close when initialization finishes; registered resources remain owned by the screen instance.</param>
        /// <param name="cancellationToken">Cancellation for initialization work; owned resources are still released if initialization is cancelled.</param>
        ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken);
        /// <summary> Prepares this route's display content and saved state before entry effects. An ordinary retained foreground return does not repeat preparation. </summary>
        ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);

        /// <summary> Starts operation of a prepared screen after transition effects, ordering, and native input admission are ready. </summary>
        /// <remarks> Establish operation subscriptions and initial or returning focus here. Every retained return receives a new activity context and valid foreground token. User input is permitted during this callback; prerequisites that must finish before operation belong in initialization, preparation, or presentation effects. Do not await a user's eventual popup answer here. </remarks>
        ValueTask ActivateAsync (TRoute route, ScreenActivityContext activity);

        /// <summary> Ends the started foreground period before departure or cover effects, including removal of operation subscriptions. </summary>
        /// <remarks> Qualification is revoked and native input is closed first. This callback is also awaited to clean up a partially failed activation. It does not end retained output, independent display subscriptions, game simulation, or binding-owned work. </remarks>
        ValueTask DeactivateAsync ();
        /// <summary>Completes final screen-specific shutdown after activity and owned work stop, while screen resources remain available.</summary>
        /// <remarks>Finish operations using initialization or preparation resources here. Those resources may be released before the handler's registered disposal. A failed termination retains its dependencies.</remarks>
        /// <param name="progress">Reports for the ending navigation operation, or an inactive reporter when no operation is running. Retained reports are ignored after termination finishes.</param>
        ValueTask TerminateAsync (NavigationProgressReporter progress);
    }
}
