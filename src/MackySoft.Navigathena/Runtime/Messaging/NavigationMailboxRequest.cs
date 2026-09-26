using System;
using System.Threading.Tasks;
using MackySoft.Navigathena.Runtime.Reservations;

namespace MackySoft.Navigathena.Runtime.Messaging
{

    internal sealed class NavigationMailboxRequest : NavigationMailboxItem
    {
        public NavigationMailboxRequest (NavigationRequest request, ReservationCoordinator.ReservationLease reservation)
        {
            Reservation = reservation;
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        public NavigationRequest Request
        {
            get;
        }
        public ReservationCoordinator.ReservationLease Reservation
        {
            get;
        }
        public TaskCompletionSource<NavigationOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NavigationOutcome Closed (NavigationState finalSnapshot) => new(new NavigationOperationId(Guid.NewGuid()), Request.Kind, NavigationOutcomeKind.Faulted, false, finalSnapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, new[] { new NavigationDiagnostic(NavigationPhase.Prepare, "The runtime is closed.") { Exception = new NavigationRuntimeClosedException() } });
    }

}
