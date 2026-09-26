using System;
using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Screens
{
    /// <summary>Provides managed acquisition and view registration while a screen factory executes.</summary>
    internal sealed class ManagedScreenCreationContext : ScreenCreationServices
    {
        private readonly ResourceScope lifetime;
        private readonly ViewRegistry views;
        private readonly RegionRouteDefinition definition;
        private bool configuringChildren;
        private ScreenPresentationBinding? presentation;
        internal List<ViewRegistration> Registrations { get; } = new();
        internal Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> ChildFactories { get; } = new();
        internal Dictionary<RegionRouteDefinitionKey, BlockerDefinition> ChildBlockers { get; } = new();

        internal ManagedScreenCreationContext (NavigationEntry entry, ResourceScope lifetime, ViewRegistry views, RegionRouteDefinition definition)
        {
            RegionId = entry.RegionId;
            this.lifetime = lifetime;
            this.views = views;
            this.definition = definition;
        }

        public override RegionInstanceId RegionId
        {
            get;
        }
        public override LifetimeContext Lifetime => lifetime.Context;
        internal IScreenLifecycleInvocation? Handler
        {
            get; private set;
        }

        public override void SetLifecycleHandler<TRoute> (IScreenLifecycleHandler<TRoute> handler)
        {
            lifetime.EnsureOpen();
            if (Handler is not null)
            {
                throw new NavigationConfigurationException("Screen construction has already returned its lifecycle handler.");
            }
            Handler = new ScreenLifecycleInvocation<TRoute>(handler);
        }

        public override void SetLifecycleHandler<TRoute, TResult> (IScreenLifecycleHandler<TRoute, TResult> handler)
        {
            lifetime.EnsureOpen();
            if (Handler is not null)
            {
                throw new NavigationConfigurationException("Screen construction has already returned its lifecycle handler.");
            }
            Handler = new ResultScreenLifecycleInvocation<TRoute, TResult>(handler);
        }
        internal IScreenAnimator? Animator => presentation?.Animator;
        internal INavigationTransitionEffect? TransitionEffect
        {
            get; private set;
        }
        internal ViewRegistration? TransitionView
        {
            get; private set;
        }

        public override void ConnectPresentation (ScreenPresentationBinding binding)
        {
            lifetime.EnsureOpen();
            if (presentation is not null)
            {
                throw new NavigationConfigurationException("A screen can connect only one primary presentation.");
            }
            ViewRegistration[] connected = views.RegisterBatch(binding.Views, binding.ReturnState.HasValue);
            presentation = binding;
            // Track the complete connection before invoking adapter code that can fail.
            Registrations.AddRange(connected);
            foreach (ViewRegistration registration in connected)
            {
                registration.ObserveLoss(lifetime.ReportLoss);
                registration.Apply(new ViewPresentation(binding.ReturnState.HasValue && registration.Original.OutputEnabled, false, registration.Original.Order));
            }
        }

        internal void ReleasePresentation ()
        {
            if (presentation?.ReturnState is ScreenAnimationState state)
            {
                Animator?.SetStateImmediately(state);
            }
            foreach (ViewRegistration registration in Registrations)
            {
                registration.Release();
            }
        }

        public override void SetTransitionEffect (INavigationTransitionEffect effect, IViewAdapter adapter)
        {
            lifetime.EnsureOpen();
            if (TransitionEffect is not null)
            {
                throw new InvalidOperationException("A transition effect is already registered for this screen.");
            }

            TransitionEffect = effect ?? throw new ArgumentNullException(nameof(effect));
            TransitionView = views.Register(adapter, false);
            TransitionView.ObserveLoss(lifetime.ReportLoss);
            TransitionView.Apply(new ViewPresentation(false, false, TransitionView.Original.Order));
        }

        public override void RegisterScreens (RegionDefinitionId region, Action<RegionScreenCatalogBuilder> configure)
        {
            lifetime.EnsureOpen();
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            if (configuringChildren)
            {
                throw new InvalidOperationException("Child registration callbacks cannot be nested.");
            }

            RegionDefinition child = definition.ChildRegions.SingleOrDefault(item => item.Id.Equals(region))
                ?? throw new NavigationConfigurationException("The region is not a child of this screen's route.");
            Dictionary<RegionRouteDefinitionKey, Func<NavigationRoute, ScreenDefinition>> staged = new(ChildFactories);
            Dictionary<RegionRouteDefinitionKey, BlockerDefinition> stagedBlockers = new(ChildBlockers);
            RegionScreenCatalogBuilder builder = new(child, staged, stagedBlockers);
            configuringChildren = true;
            try
            {
                configure(builder);
                foreach (var pair in staged)
                {
                    ChildFactories[pair.Key] = pair.Value;
                }

                foreach (var pair in stagedBlockers)
                {
                    ChildBlockers[pair.Key] = pair.Value;
                }
            }
            finally
            {
                builder.Close();
                configuringChildren = false;
            }
        }

    }
}
