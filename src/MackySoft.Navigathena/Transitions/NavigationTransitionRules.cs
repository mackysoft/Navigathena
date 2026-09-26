using System.Collections.Generic;

namespace MackySoft.Navigathena
{
    /// <summary>Registers exact route and operation matches. Equally specific matches must agree.</summary>
    public sealed class NavigationTransitionRules
    {
        private readonly List<NavigationTransitionRule> rules = new();
        public NavigationTransitionRuleBuilder On (NavigationOperationKind operation) => new(operation, rules);
        internal IReadOnlyList<NavigationTransitionRule> Snapshot () => rules.ToArray();
    }
}
