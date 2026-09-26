using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.Runtime.Planning
{

    internal sealed class NavigationPlan
    {
        public NavigationPlan (NavigationState before, NavigationState proposedAfter, NavigationOperationKind operation, RegionInstanceId target, NavigationDelta changes, IReadOnlyCollection<RegionInstanceId> readRegions, IReadOnlyCollection<RegionInstanceId> writeRegions)
        {
            Before = before;
            ProposedAfter = proposedAfter;
            Operation = operation;
            Target = target;
            Changes = changes;
            ReadRegions = readRegions;
            WriteRegions = writeRegions;
        }

        public NavigationState Before
        {
            get;
        }
        public NavigationState ProposedAfter
        {
            get;
        }
        public NavigationOperationKind Operation
        {
            get;
        }
        public RegionInstanceId Target
        {
            get;
        }
        public NavigationDelta Changes
        {
            get;
        }
        public IReadOnlyCollection<RegionInstanceId> ReadRegions
        {
            get;
        }
        public IReadOnlyCollection<RegionInstanceId> WriteRegions
        {
            get;
        }

        public bool TryMerge (NavigationState latest, out NavigationState candidate)
        {
            if (!Equals(Before.HostIncident, latest.HostIncident))
            {
                candidate = latest;
                return false;
            }

            Dictionary<RegionInstanceId, RegionState> regions = new(latest.Regions);
            Dictionary<NavigationEntryId, NavigationEntry> entries = new(latest.Entries);
            Dictionary<NavigationEntryId, PresentationState> presentations = new(latest.Presentations);

            if (!MergeTable(Before.Regions, ProposedAfter.Regions, regions, RegionEquals)
                || !MergeTable(Before.Entries, ProposedAfter.Entries, entries, Equals)
                || !MergeTable(Before.Presentations, ProposedAfter.Presentations, presentations, Equals))
            {
                candidate = latest;
                return false;
            }

            candidate = latest.With(latest.Revision + 1, regions, entries, presentations, latest.HostIncident);
            return true;
        }

        private static bool MergeTable<TKey, TValue> (IReadOnlyDictionary<TKey, TValue> before, IReadOnlyDictionary<TKey, TValue> proposed, Dictionary<TKey, TValue> latest, Func<TValue, TValue, bool> equals) where TKey : notnull
        {
            foreach (TKey key in before.Keys.Union(proposed.Keys))
            {
                bool existedBefore = before.TryGetValue(key, out TValue? beforeValue);
                bool existsAfter = proposed.TryGetValue(key, out TValue? afterValue);
                if (existedBefore == existsAfter && existedBefore && equals(beforeValue!, afterValue!))
                {
                    continue;
                }

                bool existsLatest = latest.TryGetValue(key, out TValue? latestValue);
                if (existsLatest != existedBefore || (existedBefore && !equals(latestValue!, beforeValue!)))
                {
                    return false;
                }

                if (existsAfter)
                {
                    latest[key] = afterValue!;
                }
                else
                {
                    latest.Remove(key);
                }
            }

            return true;
        }

        private static bool RegionEquals (RegionState left, RegionState right) => left.Id == right.Id && left.DefinitionId == right.DefinitionId && left.OwnerEntryId == right.OwnerEntryId && left.Entries.SequenceEqual(right.Entries);
    }

}
