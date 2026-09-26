using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Describes the complete, immutable set of presentations lost in one physical event.</summary>
    public sealed class PresentationLoss
    {
        /// <summary>Initializes a detached snapshot of the presentations lost in one physical event.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="members"/> or <paramref name="reason"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="members"/> is empty, contains a null member, or contains duplicate entry or presentation identifiers.</exception>
        public PresentationLoss (IReadOnlyList<PresentationReference> members, string reason)
        {
            if (members is null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            if (reason is null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            if (members.Count == 0)
            {
                throw new ArgumentException("At least one lost presentation is required.", nameof(members));
            }

            PresentationReference[] snapshot = new PresentationReference[members.Count];
            HashSet<NavigationEntryId> entryIds = new();
            HashSet<PresentationId> presentationIds = new();
            for (int index = 0; index < members.Count; index++)
            {
                PresentationReference member = members[index] ?? throw new ArgumentException("A loss member is required.", nameof(members));
                if (!entryIds.Add(member.EntryId))
                {
                    throw new ArgumentException("Each loss member must have a distinct entry identifier.", nameof(members));
                }

                if (!presentationIds.Add(member.PresentationId))
                {
                    throw new ArgumentException("Each loss member must have a distinct presentation identifier.", nameof(members));
                }

                snapshot[index] = member;
            }

            Members = Array.AsReadOnly(snapshot);
            Reason = reason;
        }

        /// <summary>Gets the complete detached member snapshot for this physical event.</summary>
        public IReadOnlyList<PresentationReference> Members
        {
            get;
        }

        /// <summary>Gets the diagnostic reason shared by this physical event.</summary>
        public string Reason
        {
            get;
        }
    }

}
