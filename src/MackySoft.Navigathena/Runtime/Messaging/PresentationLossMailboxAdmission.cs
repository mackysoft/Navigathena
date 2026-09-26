using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Messaging
{

    internal readonly struct PresentationLossMailboxAdmission
    {
        public PresentationLossMailboxAdmission (bool accepted, Task<PresentationLossResult> completion)
        {
            Accepted = accepted;
            Completion = completion;
        }

        public bool Accepted
        {
            get;
        }
        public Task<PresentationLossResult> Completion
        {
            get;
        }
    }

}
