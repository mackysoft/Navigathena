using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>A shareable construction identity, independent of routes, history entries and DI registrations.</summary>
    public abstract class ScreenDefinition
    {
        internal ScreenDefinition (ScreenInstancePolicy instancePolicy)
        {
            if (!Enum.IsDefined(typeof(ScreenInstancePolicy), instancePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(instancePolicy));
            }
            InstancePolicy = instancePolicy;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public ScreenHistoryReturnOptions HistoryReturn { get; init; } = new();
        public ScreenInstancePolicy InstancePolicy
        {
            get;
        }
        internal abstract Type RouteType
        {
            get;
        }
        internal abstract ValueTask CreateAsync (ScreenCreationServices creation, CancellationToken cancellationToken);
    }

    /// <summary>Constructs stable screen dependencies. Changing navigation input is passed to lifecycle methods, not construction.</summary>
    /// <remarks>The runtime checks cancellation before invoking construction and before initializing the returned handler. Construction forwards the token to ongoing work without duplicating the entry check. Cancellation does not skip cleanup of registered resources.</remarks>
    public sealed class ScreenDefinition<TRoute> : ScreenDefinition where TRoute : Route
    {
        private readonly Func<ScreenCreationContext<TRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute>>> create;
        internal override Type RouteType => typeof(TRoute);

        public ScreenDefinition (Func<ScreenCreationContext<TRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TRoute>>> create, ScreenInstancePolicy instancePolicy = ScreenInstancePolicy.Single)
            : base(instancePolicy) => this.create = create ?? throw new ArgumentNullException(nameof(create));

        internal override async ValueTask CreateAsync (ScreenCreationServices creation, CancellationToken cancellationToken)
        {
            IScreenLifecycleHandler<TRoute> handler = await create(new ScreenCreationContext<TRoute>(creation), cancellationToken)
                ?? throw new NavigationConfigurationException("Screen construction must return one lifecycle handler.");
            creation.SetLifecycleHandler(handler);
        }
    }
}
