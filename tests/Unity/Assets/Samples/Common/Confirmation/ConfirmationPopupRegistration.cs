using MackySoft.Navigathena.Unity;
using UnityEngine;

namespace MackySoft.Navigathena.Samples.Common.Confirmation
{
    /// <summary>Game-owned registration called by the host's composition code, not by each caller.</summary>
    public sealed class ConfirmationPopupRegistration : MonoBehaviour
    {
        [SerializeField] private ConfirmationPopupView prefab = null!;

        public void DefineRoutes (RootRegionDefinitionBuilder root)
        {
            root.AddRoute<ConfirmationRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
            });
        }

        public void RegisterScreens (RegionScreenCatalogBuilder screens)
        {
            ConfirmationPopupView configuredPrefab = prefab;
            screens.RegisterScreen(new ScreenDefinition<ConfirmationRoute, bool>(async (creation, token) =>
            {
                ConfirmationPopupView view = await creation.InstantiateScreenAsync(configuredPrefab, token);
                return creation.Lifetime.CreateOwned(() => new ConfirmationPresenter(view));
            }, ScreenInstancePolicy.Multiple));
        }
    }
}
