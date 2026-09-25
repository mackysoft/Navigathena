using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{
    public sealed class NavigationTransitionRuleBuilder
    {
        private readonly NavigationOperationKind operation;
        private readonly List<NavigationTransitionRule> rules;
        private Type? source;
        private Type? destination;
        private bool completed;

        internal NavigationTransitionRuleBuilder (NavigationOperationKind operation, List<NavigationTransitionRule> rules)
        {
            if (!Enum.IsDefined(typeof(NavigationOperationKind), operation))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }

            this.operation = operation;
            this.rules = rules;
        }

        public NavigationTransitionRuleBuilder From<TRoute> () where TRoute : NavigationRoute
        {
            EnsureOpen();
            source = typeof(TRoute);
            return this;
        }

        public NavigationTransitionRuleBuilder To<TRoute> () where TRoute : NavigationRoute
        {
            EnsureOpen();
            destination = typeof(TRoute);
            return this;
        }

        public void Use (NavigationTransition transition)
        {
            EnsureOpen();
            rules.Add(new NavigationTransitionRule(operation, source, destination, transition ?? throw new ArgumentNullException(nameof(transition))));
            completed = true;
        }

        private void EnsureOpen ()
        {
            if (completed)
            {
                throw new InvalidOperationException("This transition rule is already registered.");
            }
        }
    }
}
