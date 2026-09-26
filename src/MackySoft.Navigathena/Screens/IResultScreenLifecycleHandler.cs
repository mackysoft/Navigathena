using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>The single lifecycle entry point for an answering screen. Runtime gates cancellation around callbacks.</summary>
    public interface IScreenLifecycleHandler<in TRoute, TResult> where TRoute : Route<TResult>
    {
        ValueTask InitializeAsync (CancellationToken cancellationToken);
        ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);
        ValueTask ActivateAsync (TRoute route, ScreenActivityContext<TResult> activity);
        ValueTask DeactivateAsync ();
        ValueTask TerminateAsync ();
    }
}
