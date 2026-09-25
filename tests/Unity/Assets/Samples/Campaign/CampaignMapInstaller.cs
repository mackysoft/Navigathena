using MackySoft.Navigathena.MicrosoftDI;
using MackySoft.Navigathena.VContainer;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MackySoft.Navigathena.Samples.Campaign
{
    /// <summary>Scene-owned Inspector references and registration. Does not create or own a container.</summary>
    public sealed class CampaignMapInstaller : MonoBehaviour, IInstaller
    {
        [SerializeField] private CampaignMapView view = null!;
        public CampaignMapView View => view;

        public void Install (IContainerBuilder builder)
        {
            builder.RegisterInstance(view);
            builder.RegisterScreenLifecycleHandler<CampaignMapPresenter>();
        }

        public void ConfigureServices (IServiceCollection services)
        {
            services.AddSingleton(view);
            services.AddScreenLifecycleHandler<CampaignMapPresenter>();
        }
    }
}
