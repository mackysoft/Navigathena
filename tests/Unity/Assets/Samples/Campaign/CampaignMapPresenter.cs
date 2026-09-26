using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace MackySoft.Navigathena.Samples.Campaign
{
    public sealed class CampaignMapPresenter : IScreenLifecycleHandler<CampaignMapRoute>
    {
        private readonly CampaignMapView view;
        private readonly CampaignService campaign;
        private UnityAction? next;
        private UnityAction? back;

        public CampaignMapPresenter (CampaignMapView view, CampaignService campaign)
        {
            this.view = view;
            this.campaign = campaign;
        }

        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;

        public ValueTask PrepareAsync (CampaignMapRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            view.Render(campaign.GetTitle(route.ChapterId));
            return default;
        }

        public ValueTask ActivateAsync (CampaignMapRoute route, ScreenActivityContext activity)
        {
            next = async () =>
            {
                try
                {
                    await activity.Navigation.PushAsync(new CampaignMapRoute(route.ChapterId + 1));
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
            back = async () =>
            {
                try
                {
                    await activity.Navigation.BackAsync();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
            view.NextChapter.onClick.AddListener(next);
            view.Back.onClick.AddListener(back);
            return default;
        }

        public ValueTask DeactivateAsync ()
        {
            if (next is not null)
            {
                view.NextChapter.onClick.RemoveListener(next);
                next = null;
            }
            if (back is not null)
            {
                view.Back.onClick.RemoveListener(back);
                back = null;
            }
            return default;
        }

        public ValueTask TerminateAsync () => DeactivateAsync();
    }
}
