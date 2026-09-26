using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Returns the synchronous application result and host/presentation failures from a publication.</summary>
    public sealed class PublicationOutcome
    {
        private PublicationOutcome (CommitDisposition disposition, IReadOnlyList<PresentationFailure> failures, NavigationHostIncident? hostIncident)
        {
            if (disposition == CommitDisposition.HostIncident)
            {
                if (hostIncident is null || failures.Count != 0)
                {
                    throw new ArgumentException("A host-incident outcome requires exactly one host incident and no failures.");
                }
            }
            else if (hostIncident is not null)
            {
                throw new ArgumentException("Only a host-incident outcome may carry a top-level host incident.");
            }

            Disposition = disposition;
            Failures = Array.AsReadOnly(new List<PresentationFailure>(failures).ToArray());
            HostIncident = hostIncident;
        }

        public CommitDisposition Disposition
        {
            get;
        }

        public IReadOnlyList<PresentationFailure> Failures
        {
            get;
        }

        public NavigationHostIncident? HostIncident
        {
            get;
        }

        public static PublicationOutcome Applied (params PresentationFailure[] failures)
        {
            if (failures is null)
            {
                throw new ArgumentNullException(nameof(failures));
            }

            foreach (PresentationFailure failure in failures)
            {
                if (failure is null)
                {
                    throw new ArgumentException("A publication failure is required.", nameof(failures));
                }
            }

            return new PublicationOutcome(CommitDisposition.Applied, failures, null);
        }

        /// <summary>Reports a host incident that prevented the candidate state from being applied.</summary>
        public static PublicationOutcome BlockedByHostIncident (NavigationHostIncident incident) => new(CommitDisposition.HostIncident, Array.Empty<PresentationFailure>(), incident ?? throw new ArgumentNullException(nameof(incident)));

        public static PublicationOutcome Conflict { get; } = new(CommitDisposition.Conflict, Array.Empty<PresentationFailure>(), null);
    }

}
