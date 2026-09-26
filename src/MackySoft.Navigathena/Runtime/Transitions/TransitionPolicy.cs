using System;
using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Transitions
{
    internal sealed class TransitionPolicy
    {
        private readonly Dictionary<RegionDefinitionId, Policy> policies = new();

        public TransitionPolicy (NavigationDefinition definition, IReadOnlyDictionary<RegionDefinitionId, RegionNavigationOptions> regions)
        {
            foreach (KeyValuePair<RegionDefinitionId, RegionNavigationOptions> region in regions)
            {
                definition.GetRegion(region.Key);
                if (region.Value is null)
                {
                    throw new NavigationConfigurationException("Region options cannot be null.");
                }

                IReadOnlyList<NavigationTransitionRule> rules = region.Value.Transitions.Snapshot();
                foreach (NavigationTransitionRule rule in rules)
                {
                    if ((rule.Source is not null && !definition.TryGetRoute(region.Key, rule.Source, out _))
                        || (rule.Destination is not null && !definition.TryGetRoute(region.Key, rule.Destination, out _)))
                    {
                        throw new NavigationConfigurationException("A transition rule references a route not defined in its region.");
                    }
                }

                policies.Add(region.Key, new Policy(region.Value.DefaultTransition, rules));
            }
        }

        public NavigationTransition Select (PresentationTransition transition)
        {
            if (transition.Options?.Transition is NavigationTransition selected)
            {
                return selected;
            }

            if (transition.TargetRegion is not RegionInstanceId regionId || !transition.Before.Regions.TryGetValue(regionId, out RegionState? region) || !policies.TryGetValue(region.DefinitionId, out Policy? policy))
            {
                return NavigationTransition.None;
            }

            Type? source = TopRoute(transition.Before, regionId)?.GetType();
            Type? destination = TopRoute(transition.ProposedAfter, regionId)?.GetType();
            NavigationTransitionRule[] matches = policy.Rules.Where(rule => rule.Operation == transition.Operation
                && (rule.Source is null || rule.Source == source)
                && (rule.Destination is null || rule.Destination == destination)).ToArray();
            if (matches.Length == 0)
            {
                return policy.Default ?? NavigationTransition.None;
            }

            NavigationTransition[] best = matches.Where(rule => rule.Specificity == matches.Max(item => item.Specificity)).Select(rule => rule.Transition).Distinct().ToArray();
            return best.Length == 1 ? best[0] : throw new NavigationConfigurationException("Equally specific transition rules select different effects. Register an explicit source-and-destination rule.");
        }

        internal static NavigationRoute? TopRoute (NavigationState state, RegionInstanceId regionId) => state.Regions.TryGetValue(regionId, out RegionState? region) && region.Entries.Count > 0 ? state.GetEntry(region.Entries[region.Entries.Count - 1]).Route : null;
        private sealed record Policy (NavigationTransition? Default, IReadOnlyList<NavigationTransitionRule> Rules);
    }
}
