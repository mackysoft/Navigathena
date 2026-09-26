using System;
using System.Linq;
using MackySoft.Navigathena.MicrosoftDI.Ownership;
using MackySoft.Navigathena.MicrosoftDI.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace MackySoft.Navigathena.MicrosoftDI
{
    public static class ConstructionContextExtensions
    {
        /// <summary>Builds one independent provider and execution scope owned by this physical screen.</summary>
        public static IScreenLifecycleHandler<TRoute> CreateScope<TRoute> (this ScreenCreationContext<TRoute> creation, Action<IServiceCollection> configure) where TRoute : Route
            => ResolveHandler<TRoute, IScreenLifecycleHandler<TRoute>>(creation, configure);

        public static IScreenLifecycleHandler<TRoute, TResult> CreateScope<TRoute, TResult> (this ScreenCreationContext<TRoute, TResult> creation, Action<IServiceCollection> configure) where TRoute : Route<TResult>
            => ResolveHandler<TRoute, IScreenLifecycleHandler<TRoute, TResult>>(creation, configure);

        private static THandler ResolveHandler<TRoute, THandler> (ScreenCreationContext<TRoute> creation, Action<IServiceCollection> configure) where TRoute : NavigationRoute
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            MicrosoftDIScope scope = BuildScope(creation.Resources, configure);
            ScreenServiceRegistration[] handlers = scope.Roles.Where(role => role.Role == ScreenServiceRole.Lifecycle).ToArray();
            if (handlers.Length != 1 || !typeof(THandler).IsAssignableFrom(handlers[0].Type))
            {
                throw new NavigationConfigurationException("A screen scope must specify exactly one lifecycle handler accepting its route type.");
            }
            return (THandler)scope.Services.GetRequiredService(handlers[0].Type);
        }

        /// <summary>Builds a DI scope owned by the blocker, not by its current navigation input.</summary>
        public static IServiceProvider CreateScope (this BlockerPreparationContext preparation, Action<IServiceCollection> configure)
            => BuildScope((preparation ?? throw new ArgumentNullException(nameof(preparation))).Resources, configure).Services;

        /// <summary>Builds a DI scope owned by one transition effect.</summary>
        public static IServiceProvider CreateScope (this NavigationTransitionPreparationContext preparation, Action<IServiceCollection> configure)
            => BuildScope((preparation ?? throw new ArgumentNullException(nameof(preparation))).Resources, configure).Services;

        private static MicrosoftDIScope BuildScope (ResourcePreparationContext resources, Action<IServiceCollection> configure)
        {
            if (resources is null)
            {
                throw new ArgumentNullException(nameof(resources));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            MicrosoftDIScope scope = resources.CreateOwned(() => new MicrosoftDIScope());
            scope.Build(configure);
            return scope;
        }
    }
}
