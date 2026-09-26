using System;
using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Composition;

namespace MackySoft.Navigathena.Runtime.Planning
{
    internal sealed class NavigationPlanner
    {
        private readonly NavigationDefinition definition;
        private readonly Func<NavigationState, NavigationState, IReadOnlyCollection<NavigationEntryId>, NavigationState>? resolveScreens;

        public NavigationPlanner (NavigationDefinition definition, Func<NavigationState, NavigationState, IReadOnlyCollection<NavigationEntryId>, NavigationState>? resolveScreens = null)
        {
            this.definition = definition;
            this.resolveScreens = resolveScreens;
        }

        public static HistoryPrecondition ResolveReplacement (HistoryTarget target, NavigationState snapshot, RegionInstanceId region)
        {
            if (!snapshot.Regions.TryGetValue(region, out RegionState? history))
            {
                throw new NavigationConfigurationException("The target region instance does not exist.");
            }
            NavigationEntryId[] matches = target.IsCurrent ? history.Entries.TakeLast(1).ToArray()
                : history.Entries.Where(id => target.EntryId.HasValue ? id == target.EntryId.Value
                    : snapshot.GetEntry(id).Route.GetType() == target.RouteType).ToArray();
            if (matches.Length != 1)
            {
                throw new NavigationRejectionException(matches.Length == 0
                    ? "The replacement target does not exist in this region's history."
                    : "The replacement target is ambiguous. Select a specific history entry.");
            }
            return new HistoryPrecondition(matches[0], snapshot, region);
        }

        public NavigationPlan Plan (NavigationState before, NavigationOperationKind operation, RegionInstanceId target, INavigationDestinationTree? destination, CallChange? callChange = null, NavigationOptions? options = null, NavigationEntryId? replacementEntry = null, bool explicitBoundary = false)
        {
            if (before.HostIncident is not null && operation != NavigationOperationKind.Recovery)
            {
                throw new NavigationConfigurationException("The presentation host requires recovery before accepting navigation.");
            }

            if (!before.Regions.ContainsKey(target))
            {
                throw new NavigationConfigurationException("The target region instance does not exist.");
            }

            PlanWorkspace workspace = new(before, target, definition);
            workspace.Apply(operation, destination, callChange, options, replacementEntry, explicitBoundary);
            workspace.RemoveOrphanedCalls();
            NavigationState candidate = ReconcileMaterialization(workspace.Candidate);
            if (resolveScreens is not null)
            {
                candidate = resolveScreens(before, candidate, workspace.Recreated);
            }
            return new NavigationPlan(before, candidate, operation, target, workspace.Changes, GatherReadRegions(before, target), GatherWriteRegions(before, candidate, target, workspace.Removed));
        }

        private NavigationState ReconcileMaterialization (NavigationState state)
        {
            HashSet<NavigationEntryId> active = new(CompositionDeriver.GetRetainedEntries(definition, state));

            Dictionary<NavigationEntryId, PresentationState> presentations = new(state.Presentations);
            foreach (NavigationEntryId entryId in state.Entries.Keys)
            {
                PresentationState presentation = presentations[entryId];
                if (!active.Contains(entryId))
                {
                    presentations[entryId] = PresentationState.Dormant(entryId);
                }
                else if (presentation.Materialization == PresentationMaterialization.Dormant)
                {
                    presentations[entryId] = PresentationState.Available(entryId);
                }
            }

            return state.With(state.Revision, state.Regions.ToDictionary(static pair => pair.Key, static pair => pair.Value), state.Entries.ToDictionary(static pair => pair.Key, static pair => pair.Value), presentations, state.HostIncident);
        }

        private static IReadOnlyCollection<RegionInstanceId> GatherReadRegions (NavigationState state, RegionInstanceId target)
        {
            HashSet<RegionInstanceId> regions = new() { target };
            RegionState current = state.GetRegion(target);
            while (current.OwnerEntryId.HasValue)
            {
                RegionInstanceId ownerRegion = state.GetEntry(current.OwnerEntryId.Value).RegionId;
                regions.Add(ownerRegion);
                current = state.GetRegion(ownerRegion);
            }

            return regions;
        }

        private IReadOnlyCollection<RegionInstanceId> GatherWriteRegions (NavigationState before, NavigationState candidate, RegionInstanceId target, IReadOnlyList<NavigationEntry> removed)
        {
            HashSet<RegionInstanceId> regions = new() { target };
            foreach (NavigationEntry entry in removed)
            {
                regions.Add(entry.RegionId);
            }

            foreach (NavigationEntryId entryId in before.Presentations.Keys.Union(candidate.Presentations.Keys))
            {
                before.Presentations.TryGetValue(entryId, out PresentationState? previous);
                candidate.Presentations.TryGetValue(entryId, out PresentationState? next);
                if (!Equals(previous, next))
                {
                    if (before.Entries.TryGetValue(entryId, out NavigationEntry? beforeEntry))
                    {
                        regions.Add(beforeEntry.RegionId);
                    }

                    if (candidate.Entries.TryGetValue(entryId, out NavigationEntry? candidateEntry))
                    {
                        regions.Add(candidateEntry.RegionId);
                    }
                }
            }

            Dictionary<NavigationEntryId, PresentationParticipation> beforeParticipation = CompositionDeriver.Derive(definition, before).Presentations.ToDictionary(static presentation => presentation.EntryId, static presentation => presentation.Participation);
            Dictionary<NavigationEntryId, PresentationParticipation> candidateParticipation = CompositionDeriver.Derive(definition, candidate).Presentations.ToDictionary(static presentation => presentation.EntryId, static presentation => presentation.Participation);
            foreach (NavigationEntryId entryId in before.Entries.Keys.Intersect(candidate.Entries.Keys))
            {
                PresentationParticipation previous = beforeParticipation.TryGetValue(entryId, out PresentationParticipation? oldParticipation) ? oldParticipation : PresentationParticipation.Unavailable;
                PresentationParticipation next = candidateParticipation.TryGetValue(entryId, out PresentationParticipation? newParticipation) ? newParticipation : PresentationParticipation.Unavailable;
                if (previous != next)
                {
                    regions.Add(before.Entries[entryId].RegionId);
                }
            }

            return regions;
        }

        private sealed class PlanWorkspace
        {
            private readonly NavigationDefinition definition;
            private readonly RegionState targetRegion;
            private readonly RegionDefinition targetDefinition;
            private readonly Dictionary<RegionInstanceId, RegionState> regions;
            private readonly Dictionary<NavigationEntryId, NavigationEntry> entries;
            private readonly Dictionary<NavigationEntryId, PresentationState> presentations;
            private readonly List<NavigationEntryId> history;
            private readonly List<NavigationEntry> created = new();
            private readonly List<NavigationEntry> removed = new();
            private readonly NavigationState before;
            private NavigationEntryId? callOwner;
            private bool recreate;

            public PlanWorkspace (NavigationState before, RegionInstanceId target, NavigationDefinition definition)
            {
                this.before = before;
                this.definition = definition;
                targetRegion = before.GetRegion(target);
                targetDefinition = definition.GetRegion(targetRegion.DefinitionId);
                regions = new Dictionary<RegionInstanceId, RegionState>(before.Regions);
                entries = new Dictionary<NavigationEntryId, NavigationEntry>(before.Entries);
                presentations = new Dictionary<NavigationEntryId, PresentationState>(before.Presentations);
                history = targetRegion.Entries.ToList();
            }

            public IReadOnlyList<NavigationEntry> Removed => removed;
            public HashSet<NavigationEntryId> Recreated { get; } = new();
            public NavigationDelta Changes => new(created.AsReadOnly(), removed.AsReadOnly());
            public NavigationState Candidate => before.With(before.Revision, regions, entries, presentations, before.HostIncident);

            public void Apply (NavigationOperationKind operation, INavigationDestinationTree? destination, CallChange? callChange, NavigationOptions? options, NavigationEntryId? replacementEntry, bool explicitBoundary)
            {
                recreate = options?.RecreateInstance == true;
                int replacementIndex = replacementEntry.HasValue ? history.IndexOf(replacementEntry.Value) : history.Count - 1;
                if (operation == NavigationOperationKind.Replace && replacementIndex < 0)
                {
                    throw new NavigationRejectionException("The replacement target no longer exists in this region's history.");
                }
                Guid? callId = null;
                callOwner = callChange?.Owner;
                if (callChange is not null)
                {
                    callId = callChange.Id;
                    if (callChange.Kind != CallChangeKind.Open)
                    {
                        int boundary = history.FindIndex(id => entries[id].CallId == callId);
                        if (boundary < 0)
                        {
                            throw new NavigationRejectionException("The screen call has already ended.");
                        }
                        if (callChange.Kind == CallChangeKind.Close || callChange.Kind == CallChangeKind.Reset)
                        {
                            foreach (NavigationEntryId id in history.Skip(boundary).ToArray())
                            {
                                RemoveTree(id);
                            }
                            history.RemoveRange(boundary, history.Count - boundary);
                            if (callChange.Kind == CallChangeKind.Reset)
                            {
                                history.Add(CreateTree(NavigationOperationKind.Reset, targetRegion.Id, targetDefinition, RequireDestination(destination), callId));
                            }
                            regions[targetRegion.Id] = new RegionState(targetRegion.Id, targetRegion.DefinitionId, targetRegion.OwnerEntryId, history);
                            return;
                        }
                        if (entries[history[^1]].CallId != callId)
                        {
                            throw new NavigationRejectionException("A nested call is currently on top of this call.");
                        }
                    }
                }
                else if (operation == NavigationOperationKind.Replace && explicitBoundary)
                {
                    NavigationEntry selected = entries[history[replacementIndex]];
                    callId = selected.CallId;
                    callOwner = selected.CallOwnerEntryId;
                    NavigationEntry? previous = replacementIndex > 0 ? entries[history[replacementIndex - 1]]
                        : targetRegion.OwnerEntryId is NavigationEntryId parentId ? entries[parentId] : null;
                    if (callId.HasValue && previous?.CallId != callId)
                    {
                        // Removing the call's first visit ends it. Only an enclosing call survives.
                        callId = previous?.CallId;
                        callOwner = previous?.CallOwnerEntryId;
                    }
                    else if (selected.Route is not Route)
                    {
                        throw new NavigationConfigurationException("Use the typed Call.Replace to preserve the answer contract.");
                    }
                }
                else if (history.Count > 0 && (operation == NavigationOperationKind.Push || operation == NavigationOperationKind.Replace))
                {
                    NavigationEntry top = entries[history[^1]];
                    callId = top.CallId;
                    callOwner = top.CallOwnerEntryId;
                    if (operation == NavigationOperationKind.Replace && top.Route is not Route)
                    {
                        throw new NavigationConfigurationException("Use the typed Call.Replace to preserve the answer contract.");
                    }
                }
                else if (operation == NavigationOperationKind.Push && targetRegion.OwnerEntryId is NavigationEntryId parent)
                {
                    callId = entries[parent].CallId;
                    callOwner = entries[parent].CallOwnerEntryId;
                }
                switch (operation)
                {
                    case NavigationOperationKind.Push:
                        history.Add(CreateTree(operation, targetRegion.Id, targetDefinition, RequireDestination(destination), callId));
                        break;
                    case NavigationOperationKind.Replace:
                        if (history.Count == 0)
                        {
                            throw new NavigationConfigurationException("Replace requires a current entry.");
                        }

                        foreach (NavigationEntryId id in history.Skip(replacementIndex).ToArray())
                        {
                            RemoveTree(id);
                        }
                        history.RemoveRange(replacementIndex, history.Count - replacementIndex);
                        history.Add(CreateTree(operation, targetRegion.Id, targetDefinition, RequireDestination(destination), callId));
                        break;
                    case NavigationOperationKind.Reset:
                        RemoveHistory();
                        history.Add(CreateTree(operation, targetRegion.Id, targetDefinition, RequireDestination(destination), callId));
                        break;
                    case NavigationOperationKind.Back:
                        if (history.Count == 0 || (targetDefinition.Occupancy == RegionOccupancy.Required && history.Count == 1))
                        {
                            throw new NavigationRejectionException("There is no previous entry to return to.");
                        }

                        RemoveTree(history[^1]);
                        history.RemoveAt(history.Count - 1);
                        break;
                    case NavigationOperationKind.Clear:
                        if (targetDefinition.Occupancy == RegionOccupancy.Required || targetRegion.Id == before.RootRegionInstanceId)
                        {
                            throw new NavigationConfigurationException("Only an optional non-root region may be cleared.");
                        }

                        RemoveHistory();
                        break;
                    case NavigationOperationKind.Reload:
                        if (history.Count == 0)
                        {
                            throw new NavigationRejectionException("Reload requires a current entry.");
                        }
                        RecreateTree(history[^1], options?.Reload?.RestoreState == true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation));
                }

                regions[targetRegion.Id] = new RegionState(targetRegion.Id, targetRegion.DefinitionId, targetRegion.OwnerEntryId, history);
            }

            private void RemoveHistory ()
            {
                foreach (NavigationEntryId id in history.ToArray())
                {
                    RemoveTree(id);
                }

                history.Clear();
            }

            private void RecreateTree (NavigationEntryId entryId, bool restoreState)
            {
                Recreated.Add(entryId);
                if (presentations[entryId].Materialization == PresentationMaterialization.Available)
                {
                    presentations[entryId] = PresentationState.Available(entryId);
                }
                if (!restoreState)
                {
                    entries[entryId] = entries[entryId] with
                    {
                        SavedState = null
                    };
                }
                foreach (RegionState child in regions.Values.Where(region => region.OwnerEntryId == entryId))
                {
                    foreach (NavigationEntryId childEntry in child.Entries)
                    {
                        entries[childEntry] = entries[childEntry] with
                        {
                            ScreenDefinitionId = null
                        };
                        RecreateTree(childEntry, restoreState);
                    }
                }
            }

            private NavigationEntryId CreateTree (NavigationOperationKind? operation, RegionInstanceId regionId, RegionDefinition regionDefinition, INavigationDestinationTree destination, Guid? callId = null)
            {
                if (destination.Route is not Route && (callId is null || operation is null))
                {
                    throw new NavigationConfigurationException("An answering route requires a typed screen call.");
                }
                RegionRouteDefinition route = definition.GetRoute(regionDefinition.Id, destination.Route);
                RouteEntryOperations required = operation switch
                {
                    NavigationOperationKind.Push => RouteEntryOperations.Push,
                    NavigationOperationKind.Replace => RouteEntryOperations.Replace,
                    NavigationOperationKind.Reset => RouteEntryOperations.Reset,
                    null => RouteEntryOperations.None,
                    _ => throw new ArgumentOutOfRangeException(nameof(operation)),
                };
                if (required != RouteEntryOperations.None && (route.AllowedEntryOperations & required) == 0)
                {
                    throw new NavigationConfigurationException("The destination route does not permit the requested entry operation.");
                }

                HashSet<RegionDefinitionId> allowed = route.ChildRegions.Select(static child => child.Id).ToHashSet();
                if (destination.Children.Keys.Any(childId => !allowed.Contains(childId)))
                {
                    throw new NavigationConfigurationException("The destination specifies a child region not owned by its route registration.");
                }

                NavigationEntryId id = new(Guid.NewGuid());
                NavigationEntry entry = new(id, regionId, destination.Route, route.Key)
                {
                    CallId = callId,
                    CallOwnerEntryId = callOwner
                };
                entries.Add(id, entry);
                presentations.Add(id, PresentationState.Available(id));
                created.Add(entry);
                if (recreate)
                {
                    Recreated.Add(id);
                }

                foreach (RegionDefinition childDefinition in route.ChildRegions)
                {
                    if (!destination.Children.TryGetValue(childDefinition.Id, out INavigationDestinationTree? childDestination))
                    {
                        if (childDefinition.Occupancy == RegionOccupancy.Required)
                        {
                            throw new NavigationConfigurationException("A required child region needs an initial destination.");
                        }

                        RegionInstanceId emptyChild = new(Guid.NewGuid());
                        regions.Add(emptyChild, new RegionState(emptyChild, childDefinition.Id, id, Array.Empty<NavigationEntryId>()));
                        continue;
                    }

                    RegionInstanceId childRegionId = new(Guid.NewGuid());
                    NavigationEntryId childEntry = CreateTree(null, childRegionId, childDefinition, childDestination, callId);
                    regions.Add(childRegionId, new RegionState(childRegionId, childDefinition.Id, id, new[] { childEntry }));
                }

                return id;
            }

            public void RemoveOrphanedCalls ()
            {
                while (true)
                {
                    NavigationEntry[] orphaned = entries.Values.Where(entry => entry.CallOwnerEntryId is NavigationEntryId owner && !entries.ContainsKey(owner)).ToArray();
                    if (orphaned.Length == 0)
                    {
                        return;
                    }
                    foreach (NavigationEntry entry in orphaned)
                    {
                        if (!entries.ContainsKey(entry.Id))
                        {
                            continue;
                        }
                        RegionState region = regions[entry.RegionId];
                        RemoveTree(entry.Id);
                        regions[region.Id] = new RegionState(region.Id, region.DefinitionId, region.OwnerEntryId, region.Entries.Where(id => entries.ContainsKey(id)).ToArray());
                    }
                }
            }

            private void RemoveTree (NavigationEntryId entryId)
            {
                foreach (RegionState child in regions.Values.Where(region => region.OwnerEntryId == entryId).ToArray())
                {
                    foreach (NavigationEntryId childEntry in child.Entries.ToArray())
                    {
                        RemoveTree(childEntry);
                    }

                    regions.Remove(child.Id);
                }

                NavigationEntry entry = entries[entryId];
                entries.Remove(entryId);
                presentations.Remove(entryId);
                removed.Add(entry);
            }

            private static INavigationDestinationTree RequireDestination (INavigationDestinationTree? destination) => destination ?? throw new ArgumentNullException(nameof(destination));
        }
    }
}
