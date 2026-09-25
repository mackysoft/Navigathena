using System;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.MicrosoftDI;
using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.Unity;
using MackySoft.Navigathena.Unity.Addressables;
using MackySoft.Navigathena.VContainer;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;

namespace MackySoft.Navigathena.Samples.Startup
{
    /// <summary>The game calls InitializeAsync after its own initialization, and awaits ShutdownAsync before destroying this owner.</summary>
    public sealed class CampaignNavigationSetup : MonoBehaviour
    {
        public enum DependencyMode
        {
            Manual, MicrosoftDI, VContainer
        }

        [SerializeField] private AssetReference campaignScene = null!;
        [SerializeField] private StartupOverlay startupOverlay = null!;
        [SerializeField] private DependencyMode dependencies;
        private static readonly RegionDefinitionId Root = new("game");
        private readonly ResourceLifetime externalLifetime = new();
        private ServiceProvider? microsoftProvider;
        private IObjectResolver? vcontainerProvider;
        private NavigationHost? host;
        private bool initialized;

        public async Task InitializeAsync ()
        {
            if (initialized)
            {
                throw new InvalidOperationException("Navigation setup already started.");
            }
            initialized = true;
            CampaignService campaign = new();
            if (dependencies == DependencyMode.MicrosoftDI)
            {
                ServiceCollection services = new();
                services.AddSingleton(campaign);
                microsoftProvider = services.BuildServiceProvider();
            }
            else if (dependencies == DependencyMode.VContainer)
            {
                ContainerBuilder builder = new();
                builder.RegisterInstance(campaign);
                vcontainerProvider = builder.Build();
            }

            ScreenDefinition<CampaignMapRoute> screen = new(async (creation, token) =>
            {
                CampaignMapInstaller installer = await creation.LoadScreenAsync<CampaignMapInstaller>(new AddressablesSceneAcquisition(campaignScene), token);
                switch (dependencies)
                {
                    case DependencyMode.Manual:
                        return creation.Resources.CreateOwned(() => new CampaignMapPresenter(installer.View, campaign));
                    case DependencyMode.MicrosoftDI:
                        return creation.CreateScope(services =>
                        {
                            services.ImportService<CampaignService>(microsoftProvider!);
                            installer.ConfigureServices(services);
                        });
                    case DependencyMode.VContainer:
                        return creation.CreateScope(vcontainerProvider!, installer);
                    default:
                        throw new InvalidOperationException("Unknown dependency mode.");
                }
            });
            ScreenCatalog catalog = ScreenCatalog.Build(Root, RegionCompositionMode.Layered, screens => screens.Register(screen, route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            }));
            host = NavigationHost.Create(catalog);
            ResourceReference<StartupOverlay> overlay = externalLifetime.Reference(startupOverlay);
            NavigationTransition reveal = new(NavigationTransitionScope.Host, async (preparation, token) =>
            {
                StartupOverlay view = await preparation.Resources.BorrowAsync(overlay, token);
                preparation.RegisterExistingViewAdapter(view.NavigationView);
                return preparation.Resources.CreateOwned(() => new StartupRevealEffect(view));
            });
            await host.StartAsync(new CampaignMapRoute(1), new NavigationOptions { Transition = reveal });
        }

        public async ValueTask ShutdownAsync ()
        {
            if (host is not null)
            {
                await host.ShutdownAsync();
            }
            await externalLifetime.EndAsync();
            if (microsoftProvider is not null)
            {
                await microsoftProvider.DisposeAsync();
                microsoftProvider = null;
            }
            vcontainerProvider?.Dispose();
            vcontainerProvider = null;
        }
    }
}
