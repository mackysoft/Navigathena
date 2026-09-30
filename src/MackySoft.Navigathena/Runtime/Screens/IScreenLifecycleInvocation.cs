using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal interface IScreenLifecycleInvocation
    {
        object Handler { get; }
        ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken);
        ValueTask PrepareAsync (NavigationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);
        ValueTask ActivateAsync (NavigationRoute route, ScreenActivityContext activity);
        ValueTask DeactivateAsync ();
        ValueTask TerminateAsync (NavigationProgressReporter progress);
    }
}
