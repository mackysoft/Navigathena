using System;
using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Composition;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Transitions;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed partial class ScreenRuntime
    {
        private readonly Dictionary<Guid, ScreenDefinition> definitions = new();
        private readonly Dictionary<TransitionPlayback, HashSet<Guid>> definitionReservations = new();

        private ScreenDefinition? ResolveDefinition (NavigationEntry entry, NavigationState candidate)
        {
            lock (sync)
            {
                if (entry.ScreenDefinitionId is Guid id && definitions.TryGetValue(id, out ScreenDefinition? selected))
                {
                    return selected;
                }
            }

            Func<NavigationRoute, ScreenDefinition>? resolve = null;
            if (candidate.GetRegion(entry.RegionId).OwnerEntryId is NavigationEntryId parent
                && Find(candidate.GetPresentation(parent)) is ScreenInstance owner)
            {
                owner.Creation.ChildFactories.TryGetValue(entry.RouteDefinitionKey, out resolve);
            }
            if (resolve is null && !Catalog.TryResolve(entry.RouteDefinitionKey, out resolve))
            {
                return null;
            }
            ScreenDefinition definition = resolve(entry.Route);
            lock (sync)
            {
                definitions[definition.Id] = definition;
            }
            return definition;
        }

        public NavigationState ResolveScreens (NavigationState before, NavigationState candidate, IReadOnlyCollection<NavigationEntryId> recreated)
        {
            Dictionary<NavigationEntryId, NavigationEntry> entries = new(candidate.Entries);
            Dictionary<NavigationEntryId, PresentationState> presentations = new(candidate.Presentations);
            lock (sync)
            {
                ScreenInstance[] workOwners = owned.Values.Where(screen => screen.HasPendingUse && !screen.IsEnding && candidate.Entries.ContainsKey(screen.Entry.Id)).ToArray();
                if (workOwners.Any(owner => recreated.Contains(owner.Entry.Id)
                    || owned.Values.Any(screen => recreated.Contains(screen.Entry.Id) && owner.DependsOn(screen))))
                {
                    throw new NavigationConfigurationException("A screen cannot reload while a pending call or owned work still uses its instances.");
                }
                foreach (ScreenInstance screen in owned.Values.Where(screen => candidate.Entries.ContainsKey(screen.Entry.Id)
                    && workOwners.Any(owner => ReferenceEquals(owner, screen) || owner.DependsOn(screen))))
                {
                    presentations[screen.Entry.Id] = new PresentationState(screen.Entry.Id, PresentationMaterialization.Available, screen.Id, null);
                }
            }
            Dictionary<Guid, List<NavigationEntry>> singles = new();
            foreach (NavigationEntry entry in candidate.Entries.Values)
            {
                ScreenDefinition? selected = ResolveDefinition(entry, candidate);
                if (selected is null)
                {
                    continue;
                }
                entries[entry.Id] = entry with
                {
                    ScreenDefinitionId = selected.Id
                };
                (selected.HistoryReturn ?? throw new NavigationConfigurationException("A screen's history return options cannot be null.")).Validate();
                if (selected.InstancePolicy != ScreenInstancePolicy.Single)
                {
                    continue;
                }
                if (!singles.TryGetValue(selected.Id, out List<NavigationEntry>? group))
                {
                    group = new();
                    singles.Add(selected.Id, group);
                }
                group.Add(entry);
            }

            var participation = CompositionDeriver.Derive(Catalog.Definition, candidate).Presentations.ToDictionary(item => item.EntryId, item => item.Participation);
            HashSet<NavigationEntryId> reboundParents = new();
            foreach (var pair in singles.OrderBy(pair => pair.Value.Min(entry => Depth(entry.RegionId, candidate))))
            {
                NavigationEntry[] available = pair.Value.Where(entry => presentations[entry.Id].Materialization == PresentationMaterialization.Available).ToArray();
                NavigationEntry[] visible = available.Where(entry => participation[entry.Id].OutputPresented || participation[entry.Id].SemanticInputEligible).ToArray();
                if (visible.Length > 1)
                {
                    throw new NavigationConfigurationException("A single screen instance cannot display or activate multiple history entries at the same time. Use a multiple-instance definition for independent screens.");
                }
                if (available.Length == 0)
                {
                    continue;
                }
                ScreenInstance? current;
                lock (sync)
                {
                    ScreenInstance? ending = owned.Values.FirstOrDefault(screen => screen.Definition.Id == pair.Key && screen.IsEnding && !screen.IsTerminated);
                    if (ending is not null)
                    {
                        if (endings.TryGetValue(ending, out System.Threading.Tasks.Task? termination) && termination.IsFaulted)
                        {
                            throw new InvalidOperationException("The previous screen instance could not release its resources.", termination.Exception);
                        }
                        throw new NavigationConflictException("The single screen instance is still ending; its resources cannot be acquired by another instance yet.");
                    }
                    current = owned.Values.FirstOrDefault(screen => screen.Definition.Id == pair.Key && !screen.IsEnding && !screen.IsTerminated
                        && !reboundParents.Any(parent => IsDescendant(screen.Entry.RegionId, parent, before)));
                }
                NavigationEntry target = visible.FirstOrDefault() ?? available.FirstOrDefault(entry => entry.Id == current?.Entry.Id) ?? available.Last();
                if (current is not null)
                {
                    if (current.Entry.Id != target.Id)
                    {
                        if (current.HasPendingUse && candidate.Entries.ContainsKey(current.Entry.Id))
                        {
                            throw new NavigationConfigurationException("A retained history entry's pending call or owned work still uses this single screen instance.");
                        }
                        reboundParents.Add(current.Entry.Id);
                    }
                    if (!recreated.Contains(target.Id))
                    {
                        presentations[target.Id] = new PresentationState(target.Id, PresentationMaterialization.Available, current.Id, null);
                    }
                }
                foreach (NavigationEntry entry in pair.Value.Where(entry => entry.Id != target.Id))
                {
                    presentations[entry.Id] = PresentationState.Dormant(entry.Id);
                    foreach (NavigationEntry child in entries.Values.Where(child => IsDescendant(child.RegionId, entry.Id, candidate)))
                    {
                        presentations[child.Id] = PresentationState.Dormant(child.Id);
                    }
                }
            }
            return candidate.With(candidate.Revision, new Dictionary<RegionInstanceId, RegionState>(candidate.Regions), entries, presentations, candidate.HostIncident);
        }

        private void ValidateInstanceTransition (PresentationTransition transition, NavigationTransition configuration)
        {
            if (!configuration.RequiresSimultaneousScreens)
            {
                return;
            }
            foreach (PresentationState next in transition.ProposedAfter.Presentations.Values.Where(item => item.Materialization == PresentationMaterialization.Available))
            {
                if (transition.Before.Presentations.Values.Any(previous => previous.Id == next.Id && previous.EntryId != next.EntryId))
                {
                    throw new NavigationConfigurationException("This transition requires simultaneous screens but its source and destination use the same single screen instance.");
                }
            }
        }

        private void ReserveDefinitions (TransitionPlayback playback, PresentationTransition transition)
        {
            HashSet<Guid> selected = new();
            foreach (NavigationEntry entry in transition.Before.Entries.Values.Concat(transition.ProposedAfter.Entries.Values))
            {
                if (entry.ScreenDefinitionId is not Guid id || definitions[id].InstancePolicy != ScreenInstancePolicy.Single)
                {
                    continue;
                }
                transition.Before.Presentations.TryGetValue(entry.Id, out PresentationState? previous);
                transition.ProposedAfter.Presentations.TryGetValue(entry.Id, out PresentationState? next);
                if (previous != next)
                {
                    selected.Add(id);
                }
            }
            if (definitionReservations.Values.Any(reservation => reservation.Overlaps(selected)))
            {
                throw new NavigationConflictException("Another navigation operation is using the same single screen definition.");
            }
            definitionReservations.Add(playback, selected);
        }
    }
}
