using System.Collections.Generic;
using MackySoft.Navigathena.Runtime.Resources;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Blockers
{
    /// <summary>Owns resource acquisition and physical view registration during blocker preparation.</summary>
    internal sealed class ManagedBlockerPreparationContext : BlockerPreparationContext
    {
        private readonly ResourceScope resources;
        private readonly ViewRegistry views;
        internal List<ViewRegistration> Registrations { get; } = new();
        internal ManagedBlockerPreparationContext (ResourceScope resources, ViewRegistry views)
        {
            this.resources = resources;
            this.views = views;
        }
        public override ResourcePreparationContext Resources => resources.Context;
        public override void RegisterViewAdapter (IViewAdapter adapter)
        {
            resources.EnsureOpen();
            ViewRegistration registration = views.Register(adapter, false);
            Registrations.Add(registration);
            registration.ObserveLoss(resources.ReportLoss);
            registration.Apply(new ViewPresentation(false, false, registration.Original.Order));
        }
    }
}
