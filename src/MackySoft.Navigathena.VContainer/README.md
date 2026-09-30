# MackySoft.Navigathena.VContainer

Screen scope registration and ownership for VContainer.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Install VContainer from its official distribution first. The common Navigathena Runtime owns screen scope lifetime; this adapter does not implement another navigation host.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

## Define an Inspector-backed installer

Use VContainer's ordinary `IInstaller` for the screen's registrations. It can be a component in the screen scene so that its references are configured in the Inspector. This application component does not own the scope.

The following example uses the [campaign sample](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Campaign) view and presenter:

```csharp
using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.VContainer;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public sealed class CampaignInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] private CampaignMapView view = null!;

    public void Install (IContainerBuilder builder)
    {
        builder.RegisterInstance(view);
        builder.RegisterScreenLifecycleHandler<CampaignMapPresenter>();
    }
}
```

Assign the view in the Inspector. Put `ScreenPresentation` on the same GameObject as the installer when using it as the requested screen component. Its view adapters and optional animation driver may be children of that object.

`RegisterScreenLifecycleHandler<T>()` registers the presenter as the screen's single lifecycle entry point. Its constructor dependencies are resolved through VContainer. Do not register the route in the container: the same presenter can receive different route values in `PrepareAsync` and `ActivateAsync`.

## Acquire the scene and create the scope

Application setup supplies the parent `IObjectResolver`. It can be the container from an existing application `LifetimeScope`. Register shared services such as `CampaignService` there before constructing screen scopes.

```csharp
using MackySoft.Navigathena;
using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.Unity;
using MackySoft.Navigathena.Unity.Addressables;
using MackySoft.Navigathena.VContainer;

// campaignScene is an Inspector AssetReference; parentResolver is application-owned.
ScreenDefinition<CampaignMapRoute> screen = new(async (creation, token) =>
{
    CampaignInstaller installer = await creation.AcquireScreenAsync<CampaignInstaller>(
        new AddressablesSceneAcquisition(campaignScene), token);
    return creation.CreateScope(parentResolver, installer);
});
```

Register `screen` in the catalog and start the common `NavigationHost`. `CreateScope` makes a child scope from the supplied resolver, invokes the installer, and resolves the registered handler. The view is available because the scene was acquired first. There is no separate activator path or DI-specific presenter implementation.

For a prefab, pass its installer component to `AcquireScreenAsync`. For small compositions, the overload accepting `Action<IContainerBuilder>` can register the view and handler directly without an installer component.

## Result screens and lifetime

A result screen uses `ScreenDefinition<TRoute, TResult>` and `IScreenLifecycleHandler<TRoute, TResult>` with the same `CreateScope` and `RegisterScreenLifecycleHandler` calls. Missing, duplicate, mismatched, or transient lifecycle registrations are rejected before activation.

The child scope belongs to the screen instance, not a route value or activity. It participates as one owned unit in reverse registration order: acquire its scene or prefab dependencies before calling `CreateScope`. After `TerminateAsync`, later initialization registrations release before the child scope, and earlier dependencies release after it. The child scope is disposed synchronously. Put asynchronous stopping and final operations using initialization or preparation resources in the lifecycle methods; do not expect VContainer's synchronous disposal to await them. See [ownership and shutdown](https://mackysoft.github.io/Navigathena/#ownership-and-shutdown).

The parent container must outlive the host's use of it. If your application created and owns that container directly, its shutdown order is:

```csharp
await host.ShutdownAsync();
parentResolver.Dispose();
```

If the resolver belongs to a `LifetimeScope` component, let its owner dispose it after host shutdown instead of disposing it twice. Do not destroy that owner while screens are still using its services.

See [CampaignNavigationSetup](https://github.com/mackysoft/Navigathena/blob/main/tests/Unity/Assets/Samples/Startup/CampaignNavigationSetup.cs) for the same screen assembled manually, with Microsoft.Extensions.DependencyInjection, and with VContainer.
