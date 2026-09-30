using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{

    /// <summary>Describes an operation for completion observers. Public operation and recovery waits return committed results; rejection, conflict and execution failures throw <see cref="NavigationException"/>.</summary>
    public sealed record NavigationResult (NavigationOperationId OperationId, NavigationOperationKind Operation, NavigationResultKind Kind, bool DestinationCommitted, NavigationState FinalSnapshot, NavigationDelta Changes, RestorationOutcome Restoration, IReadOnlyList<NavigationDiagnostic> Diagnostics)
    {
        public NavigationPresentationStatus PresentationStatus { get; init; }

        internal void EnsureCommitted ()
        {
            if (Kind != NavigationResultKind.Committed || !DestinationCommitted)
            {
                throw new NavigationException(OperationId, Operation, DestinationCommitted, FinalSnapshot,
                    Changes, Restoration, PresentationStatus, Diagnostics,
                    new InvalidOperationException(Diagnostics.FirstOrDefault()?.Reason ?? "The navigation request was not committed."));
            }
        }
    }

}
