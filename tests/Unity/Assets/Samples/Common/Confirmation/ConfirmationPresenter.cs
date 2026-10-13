using System.Threading;
using System.Threading.Tasks;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace MackySoft.Navigathena.Samples.Common.Confirmation
{
    public sealed class ConfirmationPresenter : IScreenLifecycleHandler<ConfirmationRoute, bool>
    {
        private readonly ConfirmationPopupView view;
        private readonly EventSystem eventSystem;
        private ScreenForegroundToken foregroundToken;
        private UnityAction? accept;
        private UnityAction? decline;
        private UnityAction? close;

        public ConfirmationPresenter (ConfirmationPopupView view, EventSystem eventSystem)
        {
            this.view = view;
            this.eventSystem = eventSystem;
        }

        public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
        {
            view.ConfigureNavigation();
            return default;
        }

        public ValueTask PrepareAsync (ConfirmationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            view.Render(route);
            return default;
        }

        public ValueTask ActivateAsync (ConfirmationRoute route, ScreenActivityContext<bool> activity)
        {
            foregroundToken = activity.ForegroundToken;
            accept = () =>
            {
                if (foregroundToken.IsValid)
                {
                    activity.Call.Complete(true);
                }
            };
            decline = () =>
            {
                if (foregroundToken.IsValid)
                {
                    activity.Call.Complete(false);
                }
            };
            close = () =>
            {
                if (foregroundToken.IsValid)
                {
                    activity.Call.Dismiss();
                }
            };
            view.Accept.onClick.AddListener(accept);
            view.Decline.onClick.AddListener(decline);
            view.Close.onClick.AddListener(close);
            eventSystem.SetSelectedGameObject(view.Decline.gameObject);
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

        public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;
    }
}
