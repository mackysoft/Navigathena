using System;
using System.Collections.Generic;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Transitions
{
    /// <summary>Acquires an operation-owned effect's resources and registers its display targets.</summary>
    internal sealed class ManagedTransitionPreparationContext : NavigationTransitionPreparationContext
    {
        private readonly ResourceScope lifetime;
        private readonly ViewRegistry views;
        private readonly List<IDisposable> progressConnections = new();
        internal List<ViewRegistration> Registrations { get; } = new();

        internal ManagedTransitionPreparationContext (ResourceScope lifetime, ViewRegistry views)
        {
            this.lifetime = lifetime;
            this.views = views;
        }

        public override LifetimeContext Lifetime => lifetime.Context;
        public override void RegisterViewAdapter (IViewAdapter adapter) => Register(adapter, false);
        public override void RegisterExistingViewAdapter (IViewAdapter adapter) => Register(adapter, true);

        internal override ProgressSource<TState> CreateProgressSource<TState> (ProgressDefinition<TState> definition)
        {
            lifetime.EnsureOpen();
            ProgressSource<TState> source = lifetime.Progress.CreateSource(definition);
            progressConnections.Add(source.State);
            return source;
        }

        public override void ObserveProgress<TState> (ProgressSource<TState> source, Action<TState> receive)
        {
            lifetime.EnsureOpen();
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (receive is null)
            {
                throw new ArgumentNullException(nameof(receive));
            }
            if (!lifetime.Progress.Owns(source))
            {
                throw new NavigationConfigurationException("The progress source belongs to another navigation operation.");
            }
            progressConnections.Add(source.State.Observe(receive));
        }

        internal void EndProgress ()
        {
            for (int index = progressConnections.Count - 1; index >= 0; index--)
            {
                progressConnections[index].Dispose();
            }
            progressConnections.Clear();
        }

        private void Register (IViewAdapter adapter, bool preserve)
        {
            lifetime.EnsureOpen();
            ViewRegistration registration = views.Register(adapter, preserve);
            Registrations.Add(registration);
            registration.ObserveLoss(lifetime.ReportLoss);
            registration.Apply(new ViewPresentation(preserve && registration.Original.OutputEnabled, false, registration.Original.Order));
        }
    }
}
