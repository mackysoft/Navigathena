using System.Collections.Generic;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Blockers
{
    /// <summary>Owns resource acquisition and physical view registration during blocker preparation.</summary>
    internal sealed class ManagedBlockerPreparationContext : BlockerPreparationContext
    {
        private readonly ResourceScope lifetime;
        private readonly ViewRegistry views;
        internal List<ViewRegistration> Registrations { get; } = new();
        internal ManagedBlockerPreparationContext (ResourceScope lifetime, ViewRegistry views)
        {
            this.lifetime = lifetime;
            this.views = views;
        }
        public override LifetimeContext Lifetime => lifetime.Context;
        public override void RegisterViewAdapter (IViewAdapter adapter)
        {
            lifetime.EnsureOpen();
            ViewRegistration registration = views.Register(adapter, false);
            Registrations.Add(registration);
            registration.ObserveLoss(lifetime.ReportLoss);
            registration.Apply(new ViewPresentation(false, false, registration.Original.Order));
        }
    }
}
