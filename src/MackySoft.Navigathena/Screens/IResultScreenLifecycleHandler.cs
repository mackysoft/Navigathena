using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>The single lifecycle entry point for an answering screen. Runtime gates cancellation around callbacks.</summary>
    /// <remarks>Initialization, preparation, stopping, and resource ownership follow the same contract as a screen without a result. Activation receives the typed call endpoint only after transition effects finish.</remarks>
    public interface IScreenLifecycleHandler<in TRoute, TResult> where TRoute : Route<TResult>
    {
        /// <summary>Initializes the instance once. The context registers instance-owned resources and reports initialization progress only during this callback.</summary>
        ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken);
        ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);
        ValueTask ActivateAsync (TRoute route, ScreenActivityContext<TResult> activity);
        ValueTask DeactivateAsync ();
        /// <summary>Awaits final screen-specific stopping and reports progress for the ending operation before resources are released. This callback is not cancelled.</summary>
        ValueTask TerminateAsync (NavigationProgressReporter progress);
    }
}
