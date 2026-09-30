using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Constructs exactly one handler with a mandatory typed return contract.</summary>
    public sealed class ScreenDefinition<TRoute, TResult> : ScreenDefinition where TRoute : Route<TResult>
    {
        private readonly Func<ScreenCreationContext<TRoute, TResult>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute, TResult>>> create;

        public ScreenDefinition (Func<ScreenCreationContext<TRoute, TResult>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute, TResult>>> create, ScreenInstancePolicy instancePolicy = ScreenInstancePolicy.Single)
            : base(instancePolicy)
        {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
        }

        internal override Type RouteType => typeof(TRoute);

        internal override async ValueTask CreateAsync (ScreenCreationServices creation, CancellationToken cancellationToken)
        {
            IScreenLifecycleHandler<TRoute, TResult> handler = await create(new ScreenCreationContext<TRoute, TResult>(creation), cancellationToken)
                ?? throw new NavigationConfigurationException("Screen construction must return one lifecycle handler.");
            creation.SetLifecycleHandler(handler);
        }
    }
}
