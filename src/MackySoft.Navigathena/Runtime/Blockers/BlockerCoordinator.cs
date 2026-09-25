using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Blockers
{
    internal sealed class BlockerCoordinator
    {
        private readonly object sync = new();
        private readonly IScreenRuntimeServices runtime;
        private readonly ViewRegistry views;
        private readonly BlockerDefinition? defaultDefinition;
        private readonly HashSet<BlockerDefinition> hostDefinitions;
        private readonly Dictionary<BlockerDefinition, BlockerInstance> owned = new();
        private readonly Dictionary<BlockerDefinition, BlockerInstance> current = new();
        private readonly Dictionary<BlockerInstance, Task> endings = new();
        private readonly Dictionary<NavigationOperationId, HashSet<BlockerDefinition>> reservations = new();
        private readonly Dictionary<NavigationOperationId, List<BlockerInstance>> prepared = new();

        public BlockerCoordinator (IScreenRuntimeServices runtime, ViewRegistry views, BlockerDefinition? defaultDefinition)
        {
            this.runtime = runtime;
            this.views = views;
            this.defaultDefinition = defaultDefinition;
            hostDefinitions = new(runtime.Catalog.Blockers.Values);
            if (defaultDefinition is not null)
            {
                hostDefinitions.Add(defaultDefinition);
            }
        }

        private sealed record Selection (NavigationEntryId Entry, ScreenInstance? Owner);

        private Dictionary<BlockerDefinition, Selection> Select (NavigationState state, EffectiveComposition composition, bool allowUnprepared)
        {
            Dictionary<BlockerDefinition, Selection> selected = new();
            foreach (InputBoundary boundary in composition.InputBoundaries)
            {
                NavigationEntry entry = state.GetEntry(boundary.OwnerEntryId);
                if (runtime.Catalog.Definition.GetRoute(entry.RouteDefinitionKey.RegionId, entry.Route).LowerPresentationPolicy.Output != LowerPresentationOutput.Preserve)
                {
                    continue;
                }
                BlockerDefinition? definition = null;
                ScreenInstance? owner = null;
                if (state.GetRegion(entry.RegionId).OwnerEntryId is NavigationEntryId parent)
                {
                    owner = runtime.Find(state.GetPresentation(parent));
                    if (owner is null || owner.Entry.Id != parent)
                    {
                        if (allowUnprepared)
                        {
                            continue;
                        }
                        throw new NavigationConfigurationException("The blocker's construction owner is unavailable.");
                    }
                    owner.Creation.ChildBlockers.TryGetValue(entry.RouteDefinitionKey, out definition);
                }
                if (definition is null)
                {
                    runtime.Catalog.Blockers.TryGetValue(entry.RouteDefinitionKey, out definition);
                    definition ??= defaultDefinition;
                }
                if (definition is null)
                {
                    continue;
                }
                if (hostDefinitions.Contains(definition))
                {
                    owner = null;
                }
                if (!selected.TryAdd(definition, new Selection(entry.Id, owner)))
                {
                    throw new NavigationConfigurationException("One blocker definition cannot serve independent screens simultaneously. Register distinct blocker definitions for independent connections.");
                }
            }
            lock (sync)
            {
                foreach (var pair in selected)
                {
                    if (owned.TryGetValue(pair.Key, out BlockerInstance? instance) && !instance.IsTerminated
                        && !ReferenceEquals(instance.Parent, pair.Value.Owner))
                    {
                        throw new NavigationConfigurationException("A blocker definition is already owned by another screen instance.");
                    }
                }
            }
            return selected;
        }

        public void Validate (NavigationState state, EffectiveComposition composition) => Select(state, composition, true);

        public void Reserve (NavigationOperationId operation, NavigationState before, EffectiveComposition beforeComposition,
            NavigationState after, EffectiveComposition afterComposition, bool allowUnprepared = true)
        {
            Dictionary<BlockerDefinition, Selection> previous = Select(before, beforeComposition, true);
            Dictionary<BlockerDefinition, Selection> next = Select(after, afterComposition, allowUnprepared);
            HashSet<BlockerDefinition> changes = new(previous.Keys.Union(next.Keys).Where(definition =>
                previous.GetValueOrDefault(definition) != next.GetValueOrDefault(definition)
                || (next.TryGetValue(definition, out Selection? target)
                    && before.Presentations.GetValueOrDefault(target.Entry) != after.Presentations.GetValueOrDefault(target.Entry))));
            lock (sync)
            {
                if (reservations.Any(pair => !pair.Key.Equals(operation) && pair.Value.Overlaps(changes)))
                {
                    throw new NavigationConflictException("Another transition is changing the same shared blocker connection.");
                }
                if (!reservations.TryGetValue(operation, out HashSet<BlockerDefinition>? reserved))
                {
                    reserved = new();
                    reservations.Add(operation, reserved);
                }
                reserved.UnionWith(changes);
            }
        }

        public void Release (NavigationOperationId operation)
        {
            lock (sync)
            {
                reservations.Remove(operation);
                prepared.Remove(operation);
            }
        }

        public async ValueTask<BlockerUpdate> PrepareAsync (NavigationOperationId operation, PresentationUpdate update, CancellationToken cancellationToken)
        {
            ForgetTerminated();
            Reserve(operation, update.Before, update.BeforeComposition, update.ProposedAfter, update.ProposedComposition, false);
            Dictionary<BlockerDefinition, Selection> before = Select(update.Before, update.BeforeComposition, true);
            Dictionary<BlockerDefinition, Selection> after = Select(update.ProposedAfter, update.ProposedComposition, false);
            List<ConnectionUpdate> changes = new();
            try
            {
                foreach (BlockerDefinition definition in before.Keys.Union(after.Keys))
                {
                    Selection? previous = before.GetValueOrDefault(definition);
                    Selection? next = after.GetValueOrDefault(definition);
                    if (previous == next && !update.Changes.Any(change => (change.AfterEntry ?? change.BeforeEntry)?.Id == next?.Entry))
                    {
                        continue;
                    }
                    BlockerInstance? connected;
                    lock (sync)
                    {
                        current.TryGetValue(definition, out connected);
                    }
                    ConnectionUpdate change = new(this, definition, connected);
                    changes.Add(change);
                    connected?.CloseInput();
                    BlockerInstance? target = next is null ? null : await GetOrCreateAsync(operation, definition, next.Owner, cancellationToken);
                    bool replacement = previous is not null && after.Values.Any(selection =>
                        update.ProposedAfter.GetEntry(selection.Entry).RegionId == update.Before.GetEntry(previous.Entry).RegionId);
                    bool covered = previous is not null && (!update.ProposedAfter.Regions.ContainsKey(update.Before.GetEntry(previous.Entry).RegionId)
                        || (update.ProposedAfter.Entries.ContainsKey(previous.Entry)
                            && !update.ProposedComposition.Presentations.Any(item => item.EntryId == previous.Entry && item.Participation.OutputPresented))
                        || update.ProposedComposition.InputBoundaries.Any(boundary => boundary.RegionId == update.Before.GetEntry(previous.Entry).RegionId
                            && !update.BeforeComposition.Presentations.Any(item => item.EntryId == boundary.OwnerEntryId && item.Participation.OutputPresented)));
                    bool hadBlocker = next is not null && before.Values.Any(selection =>
                        update.Before.GetEntry(selection.Entry).RegionId == update.ProposedAfter.GetEntry(next.Entry).RegionId);
                    change.Prepare(target, next?.Entry, !replacement && !covered, !hadBlocker);
                }
                return new BlockerUpdate(this, operation, changes);
            }
            catch (Exception failure)
            {
                try
                {
                    ApplyAll(changes, change => change.Rollback());
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("Blocker preparation and restoration failed.", failure, rollbackFailure);
                }
                throw;
            }
        }

        private async ValueTask<BlockerInstance> GetOrCreateAsync (NavigationOperationId operation, BlockerDefinition definition,
            ScreenInstance? parent, CancellationToken cancellationToken)
        {
            BlockerInstance instance;
            lock (sync)
            {
                if (owned.TryGetValue(definition, out BlockerInstance? existing))
                {
                    if (existing.IsEnding)
                    {
                        throw new NavigationConflictException("The shared blocker has not safely released its previous resources.");
                    }
                    return existing;
                }
                instance = new BlockerInstance(definition.Create, views, EndUserAsync, ReportFailure, parent);
                owned.Add(definition, instance);
                if (!prepared.TryGetValue(operation, out List<BlockerInstance>? instances))
                {
                    instances = new();
                    prepared.Add(operation, instances);
                }
                instances.Add(instance);
            }
            await instance.PrepareAsync(cancellationToken);
            return instance;
        }

        public IReadOnlyList<(BlockerInstance Blocker, ScreenInstance Screen)> GetOrderPlacements (NavigationState state, EffectiveComposition composition)
        {
            Dictionary<BlockerDefinition, Selection> selected = Select(state, composition, false);
            List<(BlockerInstance, ScreenInstance)> result = new();
            KeyValuePair<BlockerDefinition, BlockerInstance>[] instances;
            lock (sync)
            {
                instances = owned.ToArray();
            }
            foreach (var pair in instances)
            {
                BlockerInstance blocker = pair.Value;
                ScreenInstance? screen = selected.TryGetValue(pair.Key, out Selection? selection)
                    ? runtime.Find(state.GetPresentation(selection.Entry)) : blocker.OrderAnchor;
                if (screen is not null && !blocker.IsTerminated && (selected.ContainsKey(pair.Key) || blocker.HasOutput))
                {
                    result.Add((blocker, screen));
                }
            }
            return result;
        }

        private async ValueTask EndUserAsync (BlockerInstance blocker)
        {
            ReportFailure(blocker, "The external owner ended a blocker resource.");
            await TerminateAsync(blocker);
        }

        private void ReportFailure (BlockerInstance blocker, string reason)
        {
            bool displayed;
            lock (sync)
            {
                displayed = current.Values.Contains(blocker);
            }
            if (displayed)
            {
                runtime.ReportEquipmentFailure(reason);
            }
        }

        public void RestoreInput (IEnumerable<ScreenInstance> screens) => ApplyInput(screens, blocker => blocker.OpenInput());
        public void CloseInput (IEnumerable<ScreenInstance> screens) => ApplyInput(screens, blocker => blocker.CloseInput());

        private void ApplyInput (IEnumerable<ScreenInstance> screens, Action<BlockerInstance> apply)
        {
            HashSet<ScreenInstance> affected = new(screens);
            BlockerInstance[] connected;
            lock (sync)
            {
                connected = current.Values.Where(blocker => blocker.Screen is ScreenInstance screen && affected.Contains(screen)).ToArray();
            }
            ApplyAll(connected, apply);
        }

        public SuspendedConnection? CaptureConnection (ScreenInstance screen)
        {
            lock (sync)
            {
                BlockerInstance? blocker = current.Values.FirstOrDefault(item => ReferenceEquals(item.Screen, screen));
                return blocker is null ? null : new SuspendedConnection(blocker, screen);
            }
        }

        internal sealed class SuspendedConnection
        {
            private readonly BlockerInstance blocker;
            private readonly ScreenInstance screen;
            public SuspendedConnection (BlockerInstance blocker, ScreenInstance screen)
            {
                this.blocker = blocker;
                this.screen = screen;
            }
            public ValueTask DisconnectAsync () => blocker.DisconnectAsync();
            public void Restore ()
            {
                blocker.Connect(screen);
                blocker.Show(false);
                blocker.OpenInput();
            }
        }

        public async ValueTask TerminateDependentsAsync (ScreenInstance parent)
        {
            BlockerInstance[] dependents;
            lock (sync)
            {
                dependents = owned.Values.Where(item => ReferenceEquals(item.Parent, parent)).ToArray();
            }
            await TerminateAllAsync(dependents);
        }

        private void ForgetTerminated ()
        {
            lock (sync)
            {
                foreach (BlockerDefinition definition in owned.Keys.Where(key => owned[key].IsTerminated).ToArray())
                {
                    endings.Remove(owned[definition]);
                    owned.Remove(definition);
                    current.Remove(definition);
                }
            }
        }

        public async ValueTask DiscardAsync (NavigationOperationId operation)
        {
            List<BlockerInstance>? instances;
            lock (sync)
            {
                prepared.Remove(operation, out instances);
            }
            if (instances is not null)
            {
                await TerminateAllAsync(instances);
            }
        }

        private ValueTask TerminateAsync (BlockerInstance blocker)
        {
            TaskCompletionSource<object?> completion;
            lock (sync)
            {
                if (blocker.IsTerminated)
                {
                    return default;
                }
                if (endings.TryGetValue(blocker, out Task? existing))
                {
                    return new ValueTask(existing);
                }
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                endings.Add(blocker, completion.Task);
            }
            _ = CompleteTerminationAsync(blocker, completion);
            return new ValueTask(completion.Task);
        }

        private async Task CompleteTerminationAsync (BlockerInstance blocker, TaskCompletionSource<object?> completion)
        {
            try
            {
                await runtime.Terminations.Track(NavigationTerminationKind.Blocker, null, null, null, blocker.TerminateAsync);
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }

        public ValueTask ShutdownAsync ()
        {
            BlockerInstance[] instances;
            lock (sync)
            {
                instances = owned.Values.ToArray();
            }
            return TerminateAllAsync(instances);
        }

        private async ValueTask TerminateAllAsync (IEnumerable<BlockerInstance> instances)
        {
            List<Exception> failures = new();
            foreach (BlockerInstance blocker in instances)
            {
                try
                {
                    await TerminateAsync(blocker);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            ForgetTerminated();
            if (failures.Count > 0)
            {
                throw new AggregateException("Blocker ownership could not be terminated.", failures);
            }
        }

        private static void ApplyAll<T> (IEnumerable<T> targets, Action<T> apply)
        {
            List<Exception> failures = new();
            foreach (T target in targets)
            {
                try
                {
                    apply(target);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Blocker connections could not all be updated.", failures);
            }
        }

        internal sealed class BlockerUpdate
        {
            private readonly BlockerCoordinator owner;
            private readonly NavigationOperationId operation;
            private readonly IReadOnlyList<ConnectionUpdate> changes;
            public BlockerUpdate (BlockerCoordinator owner, NavigationOperationId operation, IReadOnlyList<ConnectionUpdate> changes)
            {
                this.owner = owner;
                this.operation = operation;
                this.changes = changes;
            }
            public void Commit (NavigationState state)
            {
                lock (owner.sync)
                {
                    owner.prepared.Remove(operation);
                }
                ApplyAll(changes, change => change.Commit(state));
            }
            public void OpenInput () => ApplyAll(changes, change => change.OpenInput());
            public void CloseInput () => ApplyAll(changes, change => change.CloseInput());
            public void Rollback () => ApplyAll(changes, change => change.Rollback());
        }

        internal sealed class ConnectionUpdate
        {
            private readonly BlockerCoordinator owner;
            private readonly BlockerDefinition definition;
            private readonly BlockerInstance? previous;
            private BlockerInstance? next;
            private NavigationEntryId? selected;
            private bool exit;
            private bool enter;
            private bool committed;
            public ConnectionUpdate (BlockerCoordinator owner, BlockerDefinition definition, BlockerInstance? previous)
            {
                this.owner = owner;
                this.definition = definition;
                this.previous = previous;
            }
            public void Prepare (BlockerInstance? target, NavigationEntryId? entry, bool animateExit, bool animateEnter)
            {
                next = target;
                selected = entry;
                exit = animateExit;
                enter = animateEnter;
            }
            public void Commit (NavigationState state)
            {
                committed = true;
                lock (owner.sync)
                {
                    if (next is null)
                    {
                        owner.current.Remove(definition);
                    }
                    else
                    {
                        owner.current[definition] = next;
                    }
                }
                if (previous is not null && !ReferenceEquals(previous, next))
                {
                    previous.Hide(exit);
                }
                if (next is not null && selected is NavigationEntryId entry)
                {
                    ScreenInstance screen = owner.runtime.Find(state.GetPresentation(entry)) ?? throw new InvalidOperationException("The blocker screen disappeared.");
                    next.Connect(screen);
                    next.Show(enter);
                }
            }
            public void OpenInput () => next?.OpenInput();
            public void CloseInput () => next?.CloseInput();
            public void Rollback ()
            {
                if (!committed)
                {
                    previous?.OpenInput();
                }
            }
        }
    }
}
