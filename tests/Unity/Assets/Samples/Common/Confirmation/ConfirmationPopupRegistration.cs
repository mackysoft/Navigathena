using MackySoft.Navigathena.Unity;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MackySoft.Navigathena.Samples.Common.Confirmation
{
    /// <summary>Game-owned registration called by the host's composition code, not by each caller.</summary>
    public sealed class ConfirmationPopupRegistration : MonoBehaviour
    {
        [SerializeField] private ConfirmationPopupView prefab = null!;
        [SerializeField] private EventSystem eventSystem = null!;

        public void DefineRoutes (RootRegionDefinitionBuilder root)
        {
            root.AddRoute<ConfirmationRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Modal);
        }

        public void RegisterScreens (RegionScreenCatalogBuilder screens)
        {
            if (eventSystem == null)
            {
                throw new NavigationConfigurationException("Assign the confirmation popup's EventSystem before registering its screens.");
            }
            ConfirmationPopupView configuredPrefab = prefab;
            EventSystem configuredEventSystem = eventSystem;
            screens.RegisterScreen(new ScreenDefinition<ConfirmationRoute, bool>(async (creation, token) =>
            {
                ConfirmationPopupView view = await creation.AcquireScreenAsync(configuredPrefab, token);
                return creation.Lifetime.CreateOwned(() => new ConfirmationPresenter(view, configuredEventSystem));
            }, ScreenInstancePolicy.Multiple));
        }
    }
}
