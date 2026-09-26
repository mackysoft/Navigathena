using System;
using System.Linq;
using MackySoft.Navigathena.VContainer.Ownership;
using VContainer;
using VContainer.Unity;

namespace MackySoft.Navigathena.VContainer
{
    public static class ConstructionContextExtensions
    {
        public static IScreenLifecycleHandler<TRoute> CreateScope<TRoute> (this ScreenCreationContext<TRoute> creation, IObjectResolver parent, IInstaller installer) where TRoute : Route
        {
            if (installer is null)
            {
                throw new ArgumentNullException(nameof(installer));
            }
            return creation.CreateScope(parent, installer.Install);
        }

        public static IScreenLifecycleHandler<TRoute> CreateScope<TRoute> (this ScreenCreationContext<TRoute> creation, IObjectResolver parent, Action<IContainerBuilder> configure) where TRoute : Route
            => ResolveHandler<TRoute, IScreenLifecycleHandler<TRoute>>(creation, parent, configure);

        public static IScreenLifecycleHandler<TRoute, TResult> CreateScope<TRoute, TResult> (this ScreenCreationContext<TRoute, TResult> creation, IObjectResolver parent, IInstaller installer) where TRoute : Route<TResult>
        {
            if (installer is null)
            {
                throw new ArgumentNullException(nameof(installer));
            }
            return creation.CreateScope(parent, installer.Install);
        }

        public static IScreenLifecycleHandler<TRoute, TResult> CreateScope<TRoute, TResult> (this ScreenCreationContext<TRoute, TResult> creation, IObjectResolver parent, Action<IContainerBuilder> configure) where TRoute : Route<TResult>
            => ResolveHandler<TRoute, IScreenLifecycleHandler<TRoute, TResult>>(creation, parent, configure);

        private static THandler ResolveHandler<TRoute, THandler> (ScreenCreationContext<TRoute> creation, IObjectResolver parent, Action<IContainerBuilder> configure) where TRoute : NavigationRoute where THandler : class
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            THandler? handler = null;
            BuildScope(creation.Resources, parent, configure, (container, roles) =>
            {
                var handlers = roles.Items.Where(role => role.Role == ScreenServiceRole.Lifecycle).ToArray();
                if (handlers.Length != 1 || !typeof(THandler).IsAssignableFrom(handlers[0].Type))
                {
                    throw new NavigationConfigurationException("A screen scope must specify exactly one lifecycle handler accepting its route type.");
                }
                foreach (var role in roles.Items)
                {
                    if (!container.TryGetRegistration(role.Type, out Registration registration) || registration.Lifetime == Lifetime.Transient)
                    {
                        throw new NavigationConfigurationException("A screen participant must have a local, non-transient registration: " + role.Type);
                    }
                }
                handler = (THandler)container.Resolve(handlers[0].Type);
            });
            return handler ?? throw new NavigationConfigurationException("The screen scope did not resolve its lifecycle handler.");
        }

        /// <summary>Creates a managed child scope for a blocker.</summary>
        public static IObjectResolver CreateScope (this BlockerPreparationContext preparation, IObjectResolver parent, IInstaller installer)
            => BuildScope((preparation ?? throw new ArgumentNullException(nameof(preparation))).Resources, parent, installer);

        /// <summary>Creates a managed child scope for a transition effect.</summary>
        public static IObjectResolver CreateScope (this NavigationTransitionPreparationContext preparation, IObjectResolver parent, IInstaller installer)
            => BuildScope((preparation ?? throw new ArgumentNullException(nameof(preparation))).Resources, parent, installer);

        private static IObjectResolver BuildScope (ResourcePreparationContext resources, IObjectResolver parent, IInstaller installer)
        {
            if (installer is null)
            {
                throw new ArgumentNullException(nameof(installer));
            }
            return BuildScope(resources, parent, installer.Install, (_, _) =>
{
});
        }

        private static IObjectResolver BuildScope (ResourcePreparationContext resources, IObjectResolver parent, Action<IContainerBuilder> configure, Action<IObjectResolver, ScreenServiceRoles> connect)
        {
            if (resources is null)
            {
                throw new ArgumentNullException(nameof(resources));
            }
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            VContainerScope owner = resources.CreateOwned(() => new VContainerScope());
            parent.CreateScope(builder =>
            {
                ScreenServiceRoles roles = ScreenContainerBuilderExtensions.GetRoles(builder);
                // Capture the child before any user build callback can resolve services or throw.
                builder.RegisterBuildCallback(container =>
{
    owner.Container = container;
    connect(container, roles);
});
                configure(builder);
            });
            return owner.Container;
        }
    }
}
