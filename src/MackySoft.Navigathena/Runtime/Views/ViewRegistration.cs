using System;

namespace MackySoft.Navigathena.Runtime.Views
{
    internal sealed class ViewRegistration
    {
        private readonly ViewRegistry registry;
        private readonly object identity;
        private readonly bool preserve;
        private bool released;
        private int? order;
        private Action<string>? loss;

        public ViewRegistration (ViewRegistry registry, object identity, object orderingDomain, IViewAdapter adapter, bool preserve)
        {
            OrderingDomain = orderingDomain;
            this.registry = registry;
            this.identity = identity;
            this.preserve = preserve;
            Adapter = adapter;
            Original = adapter.Presentation;
        }

        public IViewAdapter Adapter
        {
            get;
        }
        public ViewPresentation Original
        {
            get;
        }
        public bool IsReleased => released;
        public int Order => order ?? Adapter.Presentation.Order;
        public object OrderingDomain
        {
            get;
        }

        public void ObserveLoss (Action<string> reportLoss)
        {
            loss = reportLoss;
            Adapter.Lost += reportLoss;
        }

        public void Validate (ViewPresentation presentation)
        {
            if (released || !Adapter.IsAlive)
            {
                throw new InvalidOperationException("The registered view is no longer available.");
            }

            if (!Equals(Adapter.Identity, identity) || !Equals(Adapter.OrderingDomain, OrderingDomain))
            {
                throw new NavigationConfigurationException("A registered view changed its native identity or ordering domain.");
            }

            Adapter.Validate(presentation);
        }

        public void Apply (ViewPresentation presentation)
        {
            presentation = new ViewPresentation(presentation.OutputEnabled, presentation.InputEnabled, Order);
            Validate(presentation);
            Adapter.Apply(presentation);
        }

        public void ValidateOrder (int value)
        {
            ViewPresentation presentation = Adapter.Presentation;
            Validate(new ViewPresentation(presentation.OutputEnabled, presentation.InputEnabled, value));
        }

        public void SetOrder (int value)
        {
            ValidateOrder(value);
            order = value;
            Apply(Adapter.Presentation);
        }

        public void Release ()
        {
            if (released)
            {
                return;
            }

            if (Adapter.IsAlive)
            {
                Adapter.Apply(preserve ? Original : new ViewPresentation(false, false, Original.Order));
            }

            Adapter.Lost -= loss;
            registry.Release(identity);
            released = true;
        }
    }
}
