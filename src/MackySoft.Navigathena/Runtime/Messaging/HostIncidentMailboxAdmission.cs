using System.Threading.Tasks;

using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Messaging
{

    internal readonly struct HostIncidentMailboxAdmission
    {
        public HostIncidentMailboxAdmission (bool accepted, Task<HostIncidentReportResult> completion)
        {
            Accepted = accepted;
            Completion = completion;
        }

        public bool Accepted
        {
            get;
        }

        public Task<HostIncidentReportResult> Completion
        {
            get;
        }
    }

}
