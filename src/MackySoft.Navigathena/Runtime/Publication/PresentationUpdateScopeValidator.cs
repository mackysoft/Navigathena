using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Composition;

namespace MackySoft.Navigathena.Runtime.Publication
{

    internal static class PresentationUpdateScopeValidator
    {
        public static bool IsCurrent (PresentationUpdate update, NavigationDefinition definition, NavigationState current)
        {
            EffectiveComposition composition = CompositionDeriver.Derive(definition, current);
            Dictionary<NavigationEntryId, PresentationParticipation> participation = composition.Presentations.ToDictionary(static item => item.EntryId, static item => item.Participation);
            foreach (PresentationChangeContext retainedChange in update.RetainedChanges)
            {
                PresentationChange self = retainedChange.Self;
                if (!MatchesEntry(current.Entries, self.BeforeEntry) || !MatchesPresentation(current.Presentations, self.Before))
                {
                    return false;
                }

                foreach (ChildRegionChange childChange in retainedChange.ChildChanges)
                {
                    if (!current.Regions.TryGetValue(childChange.InstanceId, out RegionState? region)
                        || region.DefinitionId != childChange.DefinitionId
                        || !CreateChildEntries(current, region, participation).SequenceEqual(childChange.BeforeEntries))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool MatchesEntry (IReadOnlyDictionary<NavigationEntryId, NavigationEntry> current, NavigationEntry? expected)
        {
            return expected is null || (current.TryGetValue(expected.Id, out NavigationEntry? actual) && Equals(actual, expected));
        }

        private static bool MatchesPresentation (IReadOnlyDictionary<NavigationEntryId, PresentationState> current, PresentationState? expected)
        {
            return expected is null || (current.TryGetValue(expected.EntryId, out PresentationState? actual) && Equals(actual, expected));
        }

        private static IReadOnlyList<ChildRegionEntry> CreateChildEntries (NavigationState state, RegionState region, IReadOnlyDictionary<NavigationEntryId, PresentationParticipation> participation)
        {
            return region.Entries.Select(entryId => new ChildRegionEntry(state.GetEntry(entryId), participation.TryGetValue(entryId, out PresentationParticipation? value) ? value : PresentationParticipation.Unavailable)).ToArray();
        }
    }

}
