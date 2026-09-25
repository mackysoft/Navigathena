using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.State;

namespace MackySoft.Navigathena.Runtime.Publication
{

    internal sealed class NavigationCommitCapability : INavigationCommit
    {
        private readonly NavigationStateStore state;
        private readonly CancellationToken cancellationToken;
        private readonly NavigationOperation? operation;
        private int applied;
        private int expired;
        private readonly HashSet<NavigationEntryId> repreparedEntries = new();

        internal void IncludeRepreparedEntries (IEnumerable<NavigationEntryId> entries) => repreparedEntries.UnionWith(entries);

        public NavigationCommitCapability (NavigationStateStore state, NavigationState candidate, EffectiveComposition candidateComposition, CancellationToken cancellationToken, NavigationOperation? operation = null)
        {
            this.state = state;
            Candidate = candidate;
            CandidateComposition = candidateComposition;
            this.cancellationToken = cancellationToken;
            this.operation = operation;
        }

        public NavigationState Candidate
        {
            get; private set;
        }
        public EffectiveComposition CandidateComposition
        {
            get;
        }
        public bool IsApplied => Volatile.Read(ref applied) != 0;

        internal void SelectScreenDefinitions (IReadOnlyDictionary<NavigationEntryId, Guid> selections)
        {
            if (IsApplied || Volatile.Read(ref expired) != 0)
            {
                throw new InvalidOperationException("The commit is no longer available.");
            }
            Dictionary<NavigationEntryId, NavigationEntry> entries = new(Candidate.Entries);
            foreach (var selection in selections)
            {
                NavigationEntry entry = Candidate.GetEntry(selection.Key);
                if (entry.ScreenDefinitionId is Guid selected && selected != selection.Value)
                {
                    throw new InvalidOperationException("A history entry cannot change its construction definition.");
                }
                entries[entry.Id] = entry with
                {
                    ScreenDefinitionId = selection.Value
                };
            }
            Candidate = Candidate.With(Candidate.Revision, new Dictionary<RegionInstanceId, RegionState>(Candidate.Regions), entries, new Dictionary<NavigationEntryId, PresentationState>(Candidate.Presentations), Candidate.HostIncident);
        }

        public void Apply (IReadOnlyList<PresentationStateCapture> captures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref expired) != 0 || IsApplied)
            {
                throw new InvalidOperationException("This navigation commit capability is no longer valid.");
            }

            NavigationState candidate = WithCapturedStates(captures);
            if (operation is not null)
            {
                operation.ApplyCommit(() => Publish(candidate));
            }
            else
            {
                Publish(candidate);
            }
        }

        private void Publish (NavigationState candidate)
        {
            if (Volatile.Read(ref expired) != 0 || Interlocked.CompareExchange(ref applied, 1, 0) != 0)
            {
                throw new InvalidOperationException("This navigation commit capability is no longer valid.");
            }

            Candidate = candidate;
            state.Publish(Candidate);
        }

        private NavigationState WithCapturedStates (IReadOnlyList<PresentationStateCapture> captures)
        {
            if (captures is null)
            {
                throw new ArgumentNullException(nameof(captures));
            }

            if (captures.Count == 0)
            {
                return Candidate;
            }

            NavigationState current = state.Current;
            Dictionary<NavigationEntryId, NavigationEntry> entries = new(Candidate.Entries);
            HashSet<NavigationEntryId> capturedEntries = new();
            foreach (PresentationStateCapture capture in captures)
            {
                if (capture is null
                    || !capturedEntries.Add(capture.EntryId)
                    || !current.Presentations.TryGetValue(capture.EntryId, out PresentationState? source)
                    || source.Materialization != PresentationMaterialization.Available
                    || source.Id != capture.PresentationId
                    || !entries.TryGetValue(capture.EntryId, out NavigationEntry? entry)
                    || !Candidate.Presentations.TryGetValue(capture.EntryId, out PresentationState? after)
                    || (after.Materialization == PresentationMaterialization.Available && after.Id == capture.PresentationId && !repreparedEntries.Contains(capture.EntryId)))
                {
                    throw new InvalidOperationException("A state capture must identify one current physical screen released or explicitly prepared again while its entry remains in history.");
                }

                entries[capture.EntryId] = entry with
                {
                    SavedState = capture.Value
                };
            }

            return Candidate.With(Candidate.Revision, Candidate.Regions.ToDictionary(static item => item.Key, static item => item.Value), entries, Candidate.Presentations.ToDictionary(static item => item.Key, static item => item.Value), Candidate.HostIncident);
        }

        public void Expire () => Interlocked.Exchange(ref expired, 1);
    }

}
