using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Messaging
{

    internal sealed class PresentationLossMailboxRequest : NavigationMailboxItem
    {
        public PresentationLossMailboxRequest (PresentationLoss loss)
        {
            Loss = loss;
        }

        public PresentationLoss Loss
        {
            get;
        }
        public TaskCompletionSource<PresentationLossResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

}
