using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Composition
{

    internal static class CompositionDeriver
    {
        public static EffectiveComposition Derive (NavigationDefinition definition, NavigationState state)
        {
            IReadOnlyList<ScopedPresentation> structure = GetStructure(definition, state);
            List<EffectivePresentation> effective = new(structure.Count);
            List<InputBoundary> boundaries = new();
            Dictionary<NavigationEntryId, bool> availableTrees = new();
            foreach (ScopedPresentation item in structure)
            {
                NavigationEntry entry = state.GetEntry(item.EntryId);
                PresentationState presentation = state.GetPresentation(entry.Id);
                bool available = presentation.Materialization == PresentationMaterialization.Available;
                bool treeAvailable = available && (state.GetRegion(entry.RegionId).OwnerEntryId is not NavigationEntryId owner || availableTrees[owner]);
                availableTrees.Add(entry.Id, treeAvailable);
                bool retained = item.Policy.Retention == LowerPresentationRetention.Retain;
                bool output = treeAvailable && retained && item.Policy.Output == LowerPresentationOutput.Preserve;
                bool input = treeAvailable && retained && item.Policy.Input == LowerPresentationInput.PassThrough;
                effective.Add(new EffectivePresentation(entry.Id, available
                    ? new PresentationParticipation(true, treeAvailable && item.Foreground, output, input)
                    : PresentationParticipation.Unavailable));

                LowerPresentationPolicy policy = definition.GetRoute(entry.RouteDefinitionKey.RegionId, entry.Route).LowerPresentationPolicy;
                if (output && input && policy.Input == LowerPresentationInput.Block)
                {
                    boundaries.Add(new InputBoundary(entry.RegionId, entry.Id));
                }
            }

            return new EffectiveComposition(effective.AsReadOnly(), boundaries.AsReadOnly());
        }

        public static IReadOnlyCollection<NavigationEntryId> GetRetainedEntries (NavigationDefinition definition, NavigationState state)
        {
            return GetStructure(definition, state)
                .Where(item => item.Policy.Retention == LowerPresentationRetention.Retain)
                .Select(item => item.EntryId).ToArray();
        }

        private static IReadOnlyList<ScopedPresentation> GetStructure (NavigationDefinition definition, NavigationState state)
        {
            List<ScopedPresentation> order = new();
            AppendRegion(definition, state, state.RootRegionInstanceId, LowerPresentationPolicy.Preserve, true, order);
            order.Reverse();
            return order;
        }

        private static void AppendRegion (NavigationDefinition definition, NavigationState state, RegionInstanceId regionId,
            LowerPresentationPolicy inherited, bool foreground, List<ScopedPresentation> order)
        {
            RegionState region = state.GetRegion(regionId);
            IEnumerable<NavigationEntryId> entries = definition.GetRegion(region.DefinitionId).Mode == RegionCompositionMode.Exclusive ? region.Entries.TakeLast(1) : region.Entries;
            LowerPresentationPolicy policy = inherited;
            foreach (NavigationEntryId entryId in entries.Reverse())
            {
                NavigationEntry entry = state.GetEntry(entryId);
                RegionRouteDefinition route = definition.GetRoute(region.DefinitionId, entry.Route);
                bool entryForeground = foreground && region.Entries[^1] == entryId;
                foreach (RegionDefinition child in route.ChildRegions.Reverse())
                {
                    RegionState childState = state.Regions.Values.Single(value => value.OwnerEntryId == entryId && value.DefinitionId == child.Id);
                    AppendRegion(definition, state, childState.Id, policy, entryForeground, order);
                }
                order.Add(new ScopedPresentation(entryId, policy, entryForeground));
                LowerPresentationPolicy lower = route.LowerPresentationPolicy;
                policy = new LowerPresentationPolicy(
                    policy.Output == LowerPresentationOutput.Hide ? policy.Output : lower.Output,
                    policy.Input == LowerPresentationInput.Block ? policy.Input : lower.Input,
                    policy.Retention == LowerPresentationRetention.Release ? policy.Retention : lower.Retention);
            }
        }

        private sealed record ScopedPresentation (NavigationEntryId EntryId, LowerPresentationPolicy Policy, bool Foreground);
    }

}
