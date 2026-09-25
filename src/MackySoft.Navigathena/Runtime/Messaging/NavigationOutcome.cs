using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MackySoft.Navigathena.Runtime.Messaging
{
    // Execution keeps partial outcomes until restoration and cleanup have settled.
    // Only expected outcomes may cross the public completion boundary as values.
    internal sealed record NavigationOutcome (NavigationOperationId OperationId, NavigationOperationKind Operation, NavigationOutcomeKind Kind, bool DestinationCommitted, NavigationState FinalSnapshot, NavigationDelta Changes, RestorationOutcome Restoration, IReadOnlyList<NavigationDiagnostic> Diagnostics)
    {
        public NavigationPresentationStatus PresentationStatus
        {
            get; init;
        }

        public NavigationResult GetResult (CancellationToken cancellationToken)
        {
            Exception[] errors = Diagnostics.Where(item => item.Exception is not null).Select(item => item.Exception!).Distinct().ToArray();
            if (errors.Length > 0 || Kind == NavigationOutcomeKind.Faulted || Kind == NavigationOutcomeKind.CommittedWithFault || PresentationStatus == NavigationPresentationStatus.RecoveryRequired)
            {
                Exception cause = errors.Length switch
                {
                    0 => new InvalidOperationException(Diagnostics.FirstOrDefault()?.Reason ?? "The navigation presentation requires recovery."),
                    1 => errors[0],
                    _ => new AggregateException("Navigation encountered multiple failures.", errors),
                };
                throw new NavigationException(OperationId, Operation, DestinationCommitted, FinalSnapshot, Changes, Restoration, PresentationStatus, Diagnostics, cause);
            }

            if (Kind == NavigationOutcomeKind.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            NavigationResultKind kind = Kind switch
            {
                NavigationOutcomeKind.Committed => NavigationResultKind.Committed,
                NavigationOutcomeKind.Conflict => NavigationResultKind.Conflict,
                NavigationOutcomeKind.Rejected => NavigationResultKind.Rejected,
                _ => throw new InvalidOperationException("An execution failure cannot be returned as a navigation result."),
            };
            return new NavigationResult(OperationId, Operation, kind, DestinationCommitted, FinalSnapshot, Changes, Restoration, Diagnostics) { PresentationStatus = PresentationStatus };
        }
    }

    internal enum NavigationOutcomeKind
    {
        Rejected, Conflict, Cancelled, Faulted, Committed, CommittedWithFault
    }
}
