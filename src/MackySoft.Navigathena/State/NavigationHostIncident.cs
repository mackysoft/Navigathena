using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Describes a host-level failure and the exact presentations required for its recovery.</summary>
    public sealed class NavigationHostIncident
    {
        /// <summary>Initializes a detached host-incident snapshot.</summary>
        /// <param name="id">The stable incident identifier.</param>
        /// <param name="reason">The non-empty diagnostic reason for the incident.</param>
        /// <param name="requiredPresentations">The current presentation generations required by recovery.</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> is uninitialized, <paramref name="reason"/> is empty, or a member identifier is duplicated.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="reason"/> or <paramref name="requiredPresentations"/> is <see langword="null"/>.</exception>
        public NavigationHostIncident (NavigationIncidentId id, string reason, IReadOnlyList<PresentationReference> requiredPresentations)
        {
            if (id.Value == Guid.Empty)
            {
                throw new ArgumentException("A host incident requires an incident identifier.", nameof(id));
            }

            if (reason is null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A host incident requires a non-empty reason.", nameof(reason));
            }

            if (requiredPresentations is null)
            {
                throw new ArgumentNullException(nameof(requiredPresentations));
            }

            PresentationReference[] snapshot = new PresentationReference[requiredPresentations.Count];
            HashSet<NavigationEntryId> entryIds = new();
            HashSet<PresentationId> presentationIds = new();
            for (int index = 0; index < requiredPresentations.Count; index++)
            {
                PresentationReference member = requiredPresentations[index] ?? throw new ArgumentException("A required presentation is required.", nameof(requiredPresentations));
                if (!entryIds.Add(member.EntryId))
                {
                    throw new ArgumentException("A host incident cannot require the same entry twice.", nameof(requiredPresentations));
                }

                if (!presentationIds.Add(member.PresentationId))
                {
                    throw new ArgumentException("A host incident cannot require the same presentation twice.", nameof(requiredPresentations));
                }

                snapshot[index] = member;
            }

            Id = id;
            Reason = reason;
            RequiredPresentations = Array.AsReadOnly(snapshot);
        }

        public NavigationIncidentId Id
        {
            get;
        }

        public string Reason
        {
            get;
        }

        public IReadOnlyList<PresentationReference> RequiredPresentations
        {
            get;
        }
    }

}
