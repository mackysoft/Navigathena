using System.Collections.Generic;

namespace MackySoft.Navigathena
{

    /// <summary>Returns an expected committed, rejected, or conflicting outcome. Execution failures throw <see cref="NavigationException"/>.</summary>
    public sealed record NavigationResult (NavigationOperationId OperationId, NavigationOperationKind Operation, NavigationResultKind Kind, bool DestinationCommitted, NavigationState FinalSnapshot, NavigationDelta Changes, RestorationOutcome Restoration, IReadOnlyList<NavigationDiagnostic> Diagnostics)
    {
        public NavigationPresentationStatus PresentationStatus
        {
            get; init;
        }
    }

}
