using System;
using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Composition;
using MackySoft.Navigathena.Runtime.Navigation;

namespace MackySoft.Navigathena.Runtime.Publication
{
    internal sealed class PresentationUpdateFactory
    {
        private readonly NavigationDefinition definition;
        private readonly INavigationStateSource state;
        private readonly ISourceNavigationRequestPort requests;

        public PresentationUpdateFactory (NavigationDefinition definition, INavigationStateSource state, ISourceNavigationRequestPort requests)
        {
            this.definition = definition;
            this.state = state;
            this.requests = requests;
        }

        public PresentationUpdate Create (PresentationUpdateKind kind, NavigationState before, NavigationState after, IReadOnlyCollection<RegionInstanceId>? retainedRegionScope = null)
        {
            UpdateDerivation derivation = new(before, after, retainedRegionScope, definition);
            Dictionary<NavigationEntryId, List<ChildRegionChange>> childChanges = CreateChildChanges(derivation);
            HashSet<NavigationEntryId> changedEntries = CollectChangedEntries(derivation, childChanges.Keys);
            Dictionary<NavigationEntryId, PresentationChange> changes = CreatePresentationChanges(derivation, changedEntries);
            IReadOnlyList<PresentationChangeContext> retainedChanges = CreateRetainedChanges(derivation, changes, childChanges);
            return new PresentationUpdate(kind, before, after, derivation.BeforeComposition, derivation.AfterComposition, changes.Values.ToArray(), retainedChanges);
        }

        private static HashSet<NavigationEntryId> CollectChangedEntries (UpdateDerivation derivation, IEnumerable<NavigationEntryId> ownersWithChildChanges)
        {
            HashSet<NavigationEntryId> changed = new();
            foreach (NavigationEntryId id in derivation.Before.Entries.Keys.Concat(derivation.After.Entries.Keys))
            {
                derivation.Before.Entries.TryGetValue(id, out NavigationEntry? beforeEntry);
                derivation.After.Entries.TryGetValue(id, out NavigationEntry? afterEntry);
                derivation.Before.Presentations.TryGetValue(id, out PresentationState? beforePresentation);
                derivation.After.Presentations.TryGetValue(id, out PresentationState? afterPresentation);
                derivation.BeforeParticipation.TryGetValue(id, out PresentationParticipation? beforeValue);
                derivation.AfterParticipation.TryGetValue(id, out PresentationParticipation? afterValue);
                if (!Equals(beforeEntry, afterEntry) || !Equals(beforePresentation, afterPresentation) || !Equals(beforeValue, afterValue))
                {
                    changed.Add(id);
                }
            }

            changed.UnionWith(ownersWithChildChanges);
            return changed;
        }

        private Dictionary<NavigationEntryId, PresentationChange> CreatePresentationChanges (UpdateDerivation derivation, IReadOnlyCollection<NavigationEntryId> changedEntries)
        {
            Dictionary<NavigationEntryId, PresentationChange> changes = new();
            foreach (NavigationEntryId id in changedEntries)
            {
                derivation.Before.Entries.TryGetValue(id, out NavigationEntry? beforeEntry);
                derivation.After.Entries.TryGetValue(id, out NavigationEntry? afterEntry);
                derivation.Before.Presentations.TryGetValue(id, out PresentationState? beforePresentation);
                derivation.After.Presentations.TryGetValue(id, out PresentationState? afterPresentation);
                derivation.BeforeParticipation.TryGetValue(id, out PresentationParticipation? beforeValue);
                derivation.AfterParticipation.TryGetValue(id, out PresentationParticipation? afterValue);
                PresentationContext? context = afterEntry is not null && afterPresentation?.Materialization == PresentationMaterialization.Available && afterPresentation.Id.HasValue
                    ? new PresentationContext(afterEntry.RouteDefinitionKey, afterEntry.RegionId, afterEntry.Id, afterPresentation.Id.Value, new ScreenNavigation(state, requests, derivation.After, afterEntry.Id, afterPresentation.Id.Value), afterEntry.SavedState)
                    : null;
                changes.Add(id, new PresentationChange(beforeEntry, afterEntry, beforePresentation, afterPresentation, beforeValue, afterValue, context));
            }

            return changes;
        }

        private static IReadOnlyList<PresentationChangeContext> CreateRetainedChanges (UpdateDerivation derivation, IReadOnlyDictionary<NavigationEntryId, PresentationChange> changes, IReadOnlyDictionary<NavigationEntryId, List<ChildRegionChange>> childChanges)
        {
            List<PresentationChangeContext> retainedChanges = new();
            foreach (NavigationEntryId entryId in changes.Keys)
            {
                if (!derivation.Before.Presentations.TryGetValue(entryId, out PresentationState? beforePresentation)
                    || !derivation.After.Presentations.TryGetValue(entryId, out PresentationState? afterPresentation)
                    || beforePresentation.Materialization != PresentationMaterialization.Available
                    || afterPresentation.Materialization != PresentationMaterialization.Available
                    || !beforePresentation.Id.HasValue
                    || !afterPresentation.Id.HasValue
                    || beforePresentation.Id.Value != afterPresentation.Id.Value)
                {
                    continue;
                }

                derivation.BeforeParticipation.TryGetValue(entryId, out PresentationParticipation? beforeValue);
                derivation.AfterParticipation.TryGetValue(entryId, out PresentationParticipation? afterValue);
                childChanges.TryGetValue(entryId, out List<ChildRegionChange>? children);
                if (Equals(beforeValue, afterValue) && children is not { Count: > 0 })
                {
                    continue;
                }

                retainedChanges.Add(new PresentationChangeContext(changes[entryId], children is null ? Array.Empty<ChildRegionChange>() : children));
            }

            return retainedChanges;
        }

        private static Dictionary<NavigationEntryId, List<ChildRegionChange>> CreateChildChanges (UpdateDerivation derivation)
        {
            Dictionary<NavigationEntryId, List<ChildRegionChange>> result = new();
            if (derivation.RetainedRegionScope is null)
            {
                return result;
            }

            HashSet<RegionInstanceId> scope = new(derivation.RetainedRegionScope);
            foreach (NavigationEntryId owner in derivation.Before.Entries.Keys.Intersect(derivation.After.Entries.Keys))
            {
                Dictionary<RegionDefinitionId, RegionState> beforeRegions = derivation.Before.Regions.Values.Where(region => region.OwnerEntryId == owner).ToDictionary(static region => region.DefinitionId);
                Dictionary<RegionDefinitionId, RegionState> afterRegions = derivation.After.Regions.Values.Where(region => region.OwnerEntryId == owner).ToDictionary(static region => region.DefinitionId);
                foreach (RegionDefinitionId definitionId in beforeRegions.Keys.Union(afterRegions.Keys))
                {
                    beforeRegions.TryGetValue(definitionId, out RegionState? beforeRegion);
                    afterRegions.TryGetValue(definitionId, out RegionState? afterRegion);
                    RegionInstanceId instanceId = beforeRegion?.Id ?? afterRegion!.Id;
                    if (!scope.Contains(instanceId))
                    {
                        continue;
                    }

                    IReadOnlyList<ChildRegionEntry> beforeEntries = CreateChildEntries(derivation.Before, beforeRegion, derivation.BeforeParticipation);
                    IReadOnlyList<ChildRegionEntry> afterEntries = CreateChildEntries(derivation.After, afterRegion, derivation.AfterParticipation);
                    if (beforeEntries.SequenceEqual(afterEntries))
                    {
                        continue;
                    }

                    if (!result.TryGetValue(owner, out List<ChildRegionChange>? changes))
                    {
                        changes = new List<ChildRegionChange>();
                        result.Add(owner, changes);
                    }

                    changes.Add(new ChildRegionChange(definitionId, instanceId, beforeEntries, afterEntries));
                }
            }

            return result;
        }

        private static IReadOnlyList<ChildRegionEntry> CreateChildEntries (NavigationState state, RegionState? region, IReadOnlyDictionary<NavigationEntryId, PresentationParticipation> participation) => region is null
            ? Array.Empty<ChildRegionEntry>()
            : region.Entries.Select(entryId => new ChildRegionEntry(state.GetEntry(entryId), participation.TryGetValue(entryId, out PresentationParticipation? value) ? value : PresentationParticipation.Unavailable)).ToArray();

        private sealed class UpdateDerivation
        {
            public UpdateDerivation (NavigationState before, NavigationState after, IReadOnlyCollection<RegionInstanceId>? retainedRegionScope, NavigationDefinition definition)
            {
                Before = before;
                After = after;
                RetainedRegionScope = retainedRegionScope;
                BeforeComposition = CompositionDeriver.Derive(definition, before);
                AfterComposition = CompositionDeriver.Derive(definition, after);
                BeforeParticipation = BeforeComposition.Presentations.ToDictionary(static item => item.EntryId, static item => item.Participation);
                AfterParticipation = AfterComposition.Presentations.ToDictionary(static item => item.EntryId, static item => item.Participation);
            }

            public NavigationState Before
            {
                get;
            }
            public NavigationState After
            {
                get;
            }
            public IReadOnlyCollection<RegionInstanceId>? RetainedRegionScope
            {
                get;
            }
            public EffectiveComposition BeforeComposition
            {
                get;
            }
            public EffectiveComposition AfterComposition
            {
                get;
            }
            public IReadOnlyDictionary<NavigationEntryId, PresentationParticipation> BeforeParticipation
            {
                get;
            }
            public IReadOnlyDictionary<NavigationEntryId, PresentationParticipation> AfterParticipation
            {
                get;
            }
        }
    }
}
