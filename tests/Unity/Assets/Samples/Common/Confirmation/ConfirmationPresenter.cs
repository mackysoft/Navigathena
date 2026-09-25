using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Events;

namespace MackySoft.Navigathena.Samples.Common.Confirmation
{
    public sealed class ConfirmationPresenter : IScreenLifecycleHandler<ConfirmationRoute, bool>
    {
        private readonly ConfirmationPopupView view;
        private UnityAction? accept;
        private UnityAction? decline;
        private UnityAction? close;

        public ConfirmationPresenter (ConfirmationPopupView view) => this.view = view;

        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;

        public ValueTask PrepareAsync (ConfirmationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            view.Render(route);
            return default;
        }

        public ValueTask ActivateAsync (ConfirmationRoute route, ScreenActivityContext<bool> activity)
        {
            accept = () => activity.Call.Complete(true);
            decline = () => activity.Call.Complete(false);
            close = () => activity.Call.Dismiss();
            view.Accept.onClick.AddListener(accept);
            view.Decline.onClick.AddListener(decline);
            view.Close.onClick.AddListener(close);
            return default;
        }

        public ValueTask DeactivateAsync ()
        {
            view.Accept.onClick.RemoveListener(accept);
            view.Decline.onClick.RemoveListener(decline);
            view.Close.onClick.RemoveListener(close);
            accept = null;
            decline = null;
            close = null;
            return default;
        }

        public ValueTask TerminateAsync () => default;
    }
}
