using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal interface IScreenLifecycleInvocation
    {
        object Handler
        {
            get;
        }
        ValueTask InitializeAsync (CancellationToken cancellationToken);
        ValueTask PrepareAsync (NavigationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken);
        ValueTask ActivateAsync (NavigationRoute route, ScreenActivityContext activity);
        ValueTask DeactivateAsync ();
        ValueTask TerminateAsync ();
    }

    internal sealed class ScreenLifecycleInvocation<TRoute> : IScreenLifecycleInvocation where TRoute : Route
    {
        private readonly IScreenLifecycleHandler<TRoute> handler;
        public ScreenLifecycleInvocation (IScreenLifecycleHandler<TRoute> handler) => this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        public object Handler => handler;
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => handler.InitializeAsync(cancellationToken);
        public ValueTask PrepareAsync (NavigationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => handler.PrepareAsync((TRoute)route, preparation, cancellationToken);
        public ValueTask ActivateAsync (NavigationRoute route, ScreenActivityContext activity) => handler.ActivateAsync((TRoute)route, activity);
        public ValueTask DeactivateAsync () => handler.DeactivateAsync();
        public ValueTask TerminateAsync () => handler.TerminateAsync();
    }
}
