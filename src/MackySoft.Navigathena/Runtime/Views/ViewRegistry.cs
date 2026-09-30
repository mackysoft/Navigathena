using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.Runtime.Views
{
    internal sealed class ViewRegistry
    {
        private readonly object sync = new();
        private readonly Dictionary<object, ViewRegistration> registrations = new();

        public ViewRegistration[] RegisterBatch (IReadOnlyList<IViewAdapter> adapters, bool preserve)
        {
            lock (sync)
            {
                HashSet<object> identities = new(registrations.Keys);
                object? domain = registrations.Values.FirstOrDefault()?.OrderingDomain;
                List<(object Identity, ViewRegistration Registration)> staged = new();
                foreach (IViewAdapter adapter in adapters)
                {
                    object identity = adapter.Identity ?? throw new NavigationConfigurationException("A view requires a native identity.");
                    object nextDomain = adapter.OrderingDomain ?? throw new NavigationConfigurationException("A view requires an ordering domain.");
                    if (!identities.Add(identity) || (domain is not null && !Equals(domain, nextDomain)))
                    {
                        throw new NavigationConfigurationException("The screen presentation contains a registered view or incompatible ordering domains.");
                    }
                    domain = nextDomain;
                    if (!adapter.IsAlive)
                    {
                        throw new NavigationConfigurationException("The screen presentation contains an unavailable view.");
                    }
                    adapter.Validate(new ViewPresentation(preserve && adapter.Presentation.OutputEnabled, false, adapter.Presentation.Order));
                    staged.Add((identity, new ViewRegistration(this, identity, nextDomain, adapter, preserve)));
                }
                foreach (var item in staged)
                {
                    registrations.Add(item.Identity, item.Registration);
                }
                return staged.Select(item => item.Registration).ToArray();
            }
        }

        public ViewRegistration Register (IViewAdapter adapter, bool preserve)
        {
            if (adapter is null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            object identity = adapter.Identity ?? throw new NavigationConfigurationException("A view adapter must provide a stable native identity.");
            object domain = adapter.OrderingDomain ?? throw new NavigationConfigurationException("A view adapter must identify its ordering domain.");
            ViewRegistration registration = new(this, identity, domain, adapter, preserve);
            lock (sync)
            {
                if (registrations.ContainsKey(identity))
                {
                    throw new NavigationConfigurationException("The same physical view is already registered.");
                }

                if (registrations.Values.Any(item => !Equals(item.OrderingDomain, domain)))
                {
                    throw new NavigationConfigurationException("These view adapters do not share a comparable ordering domain.");
                }

                registrations.Add(identity, registration);
            }

            return registration;
        }

        internal void Release (object identity)
        {
            lock (sync)
            {
                registrations.Remove(identity);
            }
        }
    }
}
