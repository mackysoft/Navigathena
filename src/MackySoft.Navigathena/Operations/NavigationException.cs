using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena
{
    /// <summary>An execution failure after restoration and cleanup have settled. A committed destination is not rolled back by this exception.</summary>
    public sealed class NavigationException : Exception
    {
        internal NavigationException (
            NavigationOperationId operationId,
            NavigationOperationKind operation,
            bool destinationCommitted,
            NavigationState finalSnapshot,
            NavigationDelta changes,
            RestorationOutcome restoration,
            NavigationPresentationStatus presentationStatus,
            IReadOnlyList<NavigationDiagnostic> diagnostics,
            Exception cause)
            : base("Navigation execution failed: " + cause.Message, cause)
        {
            OperationId = operationId;
            Operation = operation;
            DestinationCommitted = destinationCommitted;
            FinalSnapshot = finalSnapshot;
            Changes = changes;
            Restoration = restoration;
            PresentationStatus = presentationStatus;
            Diagnostics = diagnostics;
        }

        public NavigationOperationId OperationId
        {
            get;
        }
        public NavigationOperationKind Operation
        {
            get;
        }
        public bool DestinationCommitted
        {
            get;
        }
        public NavigationState FinalSnapshot
        {
            get;
        }
        public NavigationDelta Changes
        {
            get;
        }
        public RestorationOutcome Restoration
        {
            get;
        }
        public NavigationPresentationStatus PresentationStatus
        {
            get;
        }
        public IReadOnlyList<NavigationDiagnostic> Diagnostics
        {
            get;
        }
    }
}
