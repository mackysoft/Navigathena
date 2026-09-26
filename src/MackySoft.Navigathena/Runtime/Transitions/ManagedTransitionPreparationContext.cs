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
        internal List<ViewRegistration> Registrations { get; } = new();

        internal ManagedTransitionPreparationContext (ResourceScope lifetime, ViewRegistry views)
        {
            this.lifetime = lifetime;
            this.views = views;
        }

        public override LifetimeContext Lifetime => lifetime.Context;
        public override void RegisterViewAdapter (IViewAdapter adapter) => Register(adapter, false);
        public override void RegisterExistingViewAdapter (IViewAdapter adapter) => Register(adapter, true);

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
