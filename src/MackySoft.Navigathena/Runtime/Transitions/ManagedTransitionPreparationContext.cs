using System.Collections.Generic;
using MackySoft.Navigathena.Runtime.Resources;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Transitions
{
    /// <summary>Acquires an operation-owned effect's resources and registers its display targets.</summary>
    internal sealed class ManagedTransitionPreparationContext : NavigationTransitionPreparationContext
    {
        private readonly ResourceScope resources;
        private readonly ViewRegistry views;
        internal List<ViewRegistration> Registrations { get; } = new();

        internal ManagedTransitionPreparationContext (ResourceScope resources, ViewRegistry views)
        {
            this.resources = resources;
            this.views = views;
        }

        public override ResourcePreparationContext Resources => resources.Context;
        public override void RegisterViewAdapter (IViewAdapter adapter) => Register(adapter, false);
        public override void RegisterExistingViewAdapter (IViewAdapter adapter) => Register(adapter, true);

        private void Register (IViewAdapter adapter, bool preserve)
        {
            resources.EnsureOpen();
            ViewRegistration registration = views.Register(adapter, preserve);
            Registrations.Add(registration);
            registration.ObserveLoss(resources.ReportLoss);
            registration.Apply(new ViewPresentation(preserve && registration.Original.OutputEnabled, false, registration.Original.Order));
        }
    }
}
