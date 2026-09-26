using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Messaging
{

    internal sealed class NavigationRecoveryMailboxRequest : NavigationMailboxItem
    {
        public NavigationRecoveryMailboxRequest (NavigationIncidentId incidentId, CancellationToken cancellationToken)
        {
            IncidentId = incidentId;
            CancellationToken = cancellationToken;
        }

        public NavigationIncidentId IncidentId
        {
            get;
        }
        public CancellationToken CancellationToken
        {
            get;
        }
        public TaskCompletionSource<NavigationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NavigationOutcome Closed (NavigationState finalSnapshot) => new(new NavigationOperationId(Guid.NewGuid()), NavigationOperationKind.Recovery, NavigationOutcomeKind.Faulted, false, finalSnapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, new[] { new NavigationDiagnostic(NavigationPhase.Prepare, "The runtime is closed.") { Exception = new NavigationRuntimeClosedException() } });
    }

}
