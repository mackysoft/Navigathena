using System;

namespace MackySoft.Navigathena
{

    /// <summary>Identifies an exact physical presentation generation together with its logical entry, independently of its availability.</summary>
    public sealed record PresentationReference
    {
        /// <summary>Initializes a correlation between a logical entry and its physical presentation generation.</summary>
        /// <exception cref="ArgumentException"><paramref name="entryId"/> or <paramref name="presentationId"/> is not initialized.</exception>
        public PresentationReference (NavigationEntryId entryId, PresentationId presentationId)
        {
            if (entryId.Value == Guid.Empty)
            {
                throw new ArgumentException("An entry identifier is required.", nameof(entryId));
            }

            if (presentationId.Value == Guid.Empty)
            {
                throw new ArgumentException("A presentation identifier is required.", nameof(presentationId));
            }

            EntryId = entryId;
            PresentationId = presentationId;
        }

        /// <summary>Gets the logical entry that owns the referenced presentation.</summary>
        public NavigationEntryId EntryId
        {
            get;
        }

        /// <summary>Gets the exact physical presentation generation being referenced.</summary>
        public PresentationId PresentationId
        {
            get;
        }
    }

}
