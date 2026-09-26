using System;
using System.Threading.Tasks;

using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Messaging
{

    internal sealed class HostIncidentMailboxRequest : NavigationMailboxItem
    {
        public HostIncidentMailboxRequest (NavigationHostIncident incident)
        {
            Incident = incident ?? throw new ArgumentNullException(nameof(incident));
        }

        public NavigationHostIncident Incident
        {
            get;
        }

        internal long AdmissionOrder
        {
            get; set;
        }

        internal long AdmissionWatermark
        {
            get; set;
        }

        public TaskCompletionSource<HostIncidentReportResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
