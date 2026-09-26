using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal sealed class ResultScreenLifecycleInvocation<TRoute, TResult> : IScreenLifecycleInvocation where TRoute : Route<TResult>
    {
        private readonly IScreenLifecycleHandler<TRoute, TResult> handler;

        public ResultScreenLifecycleInvocation (IScreenLifecycleHandler<TRoute, TResult> handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public object Handler => handler;
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => handler.InitializeAsync(cancellationToken);
        public ValueTask PrepareAsync (NavigationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => handler.PrepareAsync((TRoute)route, preparation, cancellationToken);
        public ValueTask ActivateAsync (NavigationRoute route, ScreenActivityContext activity) => handler.ActivateAsync((TRoute)route, new ScreenActivityContext<TResult>(activity));
        public ValueTask DeactivateAsync () => handler.DeactivateAsync();
        public ValueTask TerminateAsync () => handler.TerminateAsync();
    }
}
