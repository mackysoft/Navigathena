# Navigathena Microsoft.Extensions.DependencyInjection adapter

Install `MackySoft.Navigathena.Extensions.DependencyInjection` alongside the common runtime. In Unity, restore it with NuGetForUnity alongside the Unity adapter. Add these namespaces in composition code:

```csharp
using MackySoft.Navigathena;
using MackySoft.Navigathena.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
```

## Register a screen

Return the result of `creation.CreateScope(services => …)` from the factory of `ScreenDefinition<TRoute>`.
Register the screen's single lifecycle entry point with `services.AddScreenLifecycleHandler<Presenter>()`. The container resolves its constructor dependencies through normal DI resolution.
A missing handler, multiple handlers, or a mismatched route type causes a configuration error before initialization.
Do not register the route in the container. Receive it through the arguments to `PrepareAsync` and `ActivateAsync`.

This factory uses the application-defined types from the [Unity campaign sample](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Campaign). `campaignScene` is an Inspector `AssetReference`; `applicationProvider` is the application's already-built provider containing `CampaignService`.

```csharp
using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.Unity;
using MackySoft.Navigathena.Unity.Addressables;

ScreenDefinition<CampaignMapRoute> screen = new(async (creation, token) =>
{
    CampaignMapView view = await creation.AcquireScreenAsync<CampaignMapView>(
        new AddressablesSceneAcquisition(campaignScene), token);

    return creation.CreateScope(services =>
    {
        services.AddSingleton(view);
        services.ImportService<CampaignService>(applicationProvider);
        services.AddScreenLifecycleHandler<CampaignMapPresenter>();
    });
});
```

Register `screen` with the catalog, then create and start the ordinary `NavigationHost`. The adapter does not create another host. The presenter receives the view and service through its normal constructor. It receives each route only when its lifecycle methods run.

`AddSingleton(view)` registers an existing object; it does not transfer destruction of the scene object to the container. The scene acquisition still owns that scene. Do not add a second ownership registration for the resolved presenter.

## Keep Inspector registration in the scene

If several references live in the loaded scene, put the registration code on an application installer component in that scene. The [CampaignMapInstaller](https://github.com/mackysoft/Navigathena/blob/main/tests/Unity/Assets/Samples/Campaign/CampaignMapInstaller.cs) example exposes `ConfigureServices(IServiceCollection)`; this is an application method, not a required Navigathena interface.

```csharp
CampaignMapInstaller installer = await creation.AcquireScreenAsync<CampaignMapInstaller>(
    new AddressablesSceneAcquisition(campaignScene), token);

return creation.CreateScope(services =>
{
    services.ImportService<CampaignService>(applicationProvider);
    installer.ConfigureServices(services);
});
```

The installer holds serialized references and adds registrations. Navigathena owns the resulting scope's lifetime. For a non-Unity application, omit scene acquisition and register the application's views and dependencies in the same way.

## Result screens

For screens that return a result, implement `IScreenLifecycleHandler<TRoute, TResult>`.
Factories registered through `ScreenCatalogDefinitionBuilder.Register<TRoute, TResult>` receive the same `ScreenCreationContext<TRoute, TResult>` as `ScreenDefinition<TRoute, TResult>`, so they can use `creation.CreateScope(services => …)` in the same way.

For example, register the [confirmation presenter](https://github.com/mackysoft/Navigathena/blob/main/tests/Unity/Assets/Samples/Common/Confirmation/ConfirmationPresenter.cs) with `services.AddScreenLifecycleHandler<ConfirmationPresenter>()`. The adapter checks its `ConfirmationRoute` / `bool` contract when constructing the screen. The caller still uses `InvokeAsync`, and the presenter still answers through `ScreenActivityContext<bool>.Call`.

## Scope ownership and shutdown

Each screen instance receives independent service registrations, a service provider, and an asynchronously disposable service scope.
`ImportService<T>(applicationProvider)` borrows an existing instance; closing the screen does not dispose of that service.
Dispose of the application provider only after the navigation host has shut down successfully.

Microsoft.Extensions.DependencyInjection does not provide nested registration containers through a normal `IServiceScope`. This adapter creates an independent provider for each screen; it does not copy all registrations from the application provider. Import the shared services the screen needs explicitly. Imports must outlive the screen's use of them.

Reusing a screen instance for another history entry does not rebuild its DI scope.
The DI scope participates as one owned unit in the screen lifetime's reverse registration order. Acquire its dependencies before calling `CreateScope`. After lifecycle termination, later initialization registrations release before the scope; the runtime then awaits asynchronous scope disposal before releasing its earlier dependencies. Use `TerminateAsync` for final operations that need initialization or preparation resources. See [ownership and shutdown](https://mackysoft.github.io/Navigathena/#ownership-and-shutdown).

```csharp
await host.ShutdownAsync();
await applicationProvider.DisposeAsync();
```

The second line must not run if host shutdown fails to stop users safely. The application owns its provider and startup/shutdown flow; the screen presenter must not dispose of that provider or its shared services.
