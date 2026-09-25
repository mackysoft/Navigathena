using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Describes one adapter failure and its exact current physical correlation.</summary>
    public sealed class PresentationFailure
    {
        public PresentationFailure (
            PresentationFailureScope scope,
            NavigationPhase phase,
            string reason,
            IReadOnlyList<PresentationReference> affectedPresentations,
            string? surfaceDiagnostic = null,
            NavigationHostIncident? hostIncident = null)
        {
            if (reason is null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A presentation failure requires a non-empty reason.", nameof(reason));
            }

            if (affectedPresentations is null)
            {
                throw new ArgumentNullException(nameof(affectedPresentations));
            }

            PresentationReference[] snapshot = new PresentationReference[affectedPresentations.Count];
            HashSet<NavigationEntryId> entries = new();
            HashSet<PresentationId> presentations = new();
            for (int index = 0; index < affectedPresentations.Count; index++)
            {
                PresentationReference member = affectedPresentations[index] ?? throw new ArgumentException("An affected presentation is required.", nameof(affectedPresentations));
                if (!entries.Add(member.EntryId) || !presentations.Add(member.PresentationId))
                {
                    throw new ArgumentException("Affected presentations must have unique entry and presentation identifiers.", nameof(affectedPresentations));
                }

                snapshot[index] = member;
            }

            if (scope == PresentationFailureScope.CurrentPresentations)
            {
                if (snapshot.Length == 0 || hostIncident is not null)
                {
                    throw new ArgumentException("A current-presentation failure requires members and cannot carry a host incident.", nameof(affectedPresentations));
                }
            }
            else if (scope == PresentationFailureScope.PresentationHost)
            {
                if (snapshot.Length != 0 || hostIncident is null || !string.Equals(reason, hostIncident.Reason, StringComparison.Ordinal))
                {
                    throw new ArgumentException("A host failure requires no presentation members, an incident, and the incident reason.", nameof(affectedPresentations));
                }
            }
            else if (hostIncident is not null)
            {
                throw new ArgumentException("A retired-resource failure cannot carry a host incident.", nameof(hostIncident));
            }

            Scope = scope;
            Phase = phase;
            Reason = reason;
            AffectedPresentations = Array.AsReadOnly(snapshot);
            SurfaceDiagnostic = surfaceDiagnostic;
            HostIncident = hostIncident;
        }

        public PresentationFailureScope Scope
        {
            get;
        }

        public NavigationPhase Phase
        {
            get;
        }

        public string Reason
        {
            get;
        }

        public Exception? Exception
        {
            get; init;
        }

        public IReadOnlyList<PresentationReference> AffectedPresentations
        {
            get;
        }

        public string? SurfaceDiagnostic
        {
            get;
        }

        public NavigationHostIncident? HostIncident
        {
            get;
        }
    }

}
