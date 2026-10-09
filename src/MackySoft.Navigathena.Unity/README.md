# MackySoft.Navigathena.Unity

Connects Unity scenes, prefabs, configured screen views, and Animator playback to the common Navigathena runtime. There is no separate Unity navigation host: create `NavigationHost` from `MackySoft.Navigathena.Hosting`.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

## Requirements

Use Unity 2021.3 or newer, with the built-in Animation module enabled.

- On Unity versions earlier than 2023.1, install [UniTask](https://github.com/Cysharp/UniTask) 2.1 or newer.
- On Unity 2023.1 and newer, UniTask is optional. Navigathena uses UniTask for frame waits when available, including on Unity 6; otherwise it uses Unity's `Awaitable`.

Installing UniTask as the `com.cysharp.unitask` UPM package enables its use automatically. If you copy UniTask into `Assets` instead, add `NAVIGATHENA_UNITASK` to **Scripting Define Symbols** in Player Settings and keep its `UniTask` assembly definition.

Install adapter dependencies separately, choosing versions that support your Unity Editor. NuGet does not install UniTask, uGUI, Addressables, or VContainer for you. The common Navigathena runtime does not depend on UniTask, and public asynchronous APIs continue to use `Task` and `ValueTask`.

Verified with Unity 2021.3.45f2 and UniTask 2.5.11, and Unity 6000.5.5f1 with and without UniTask 2.5.11, using NuGetForUnity 4.5.0.

## Configure a screen in the Inspector

For a uGUI screen, create a scene object or prefab with:

```text
Campaign screen
  CampaignMapView              Application labels, buttons, and rendering methods
  ScreenPresentation          Navigathena presentation configuration
  Canvas                      Independent screen-space root Canvas
  GraphicRaycaster
  CanvasViewAdapter           Visibility, input boundary, and sorting
  Motion root
    Animator                  Optional screen animation
    AnimatorScreenAnimationDriver
```

1. Assign the application's labels and buttons to `CampaignMapView`.
2. Add `CanvasViewAdapter` from the uGUI adapter package and leave **Present Before Navigation** off for a newly acquired screen.
3. Add that adapter to **Views** in `ScreenPresentation`.
4. If the screen animates, assign an `IScreenAnimator` component, such as `AnimatorScreenAnimationDriver`, to **Animator** in `ScreenPresentation`.
5. Use **Validate Screen Presentation** from the component's context menu to check its references. All referenced components must belong to this screen's hierarchy.

For UI Toolkit, use its [screen acquisition and input-scope API](../MackySoft.Navigathena.Unity.UIToolkit/README.md). Its presentation host creates a separate native panel for each view and connects input-closed content before returning it to the screen factory. Do not disable the root GameObject to hide an active managed screen: Navigathena controls output and input through the adapter. Application subscriptions still belong to `ActivateAsync` and `DeactivateAsync`.

The requested view or installer component and `ScreenPresentation` must be on the same GameObject. The view need not expose adapter or animator properties just to connect the screen.

## Instantiate a configured prefab

The application setup component owns Inspector references. Its factory acquires the configured screen and returns a presenter. The example uses the [campaign sample's](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Campaign) `CampaignMapView`, `CampaignMapPresenter`, `CampaignMapRoute`, and `CampaignService`.

```csharp
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.Unity;
using UnityEngine;

public sealed class CampaignSetup : MonoBehaviour
{
    [SerializeField] private CampaignMapView campaignPrefab = null!;
    private NavigationHost? host;

    public async Task InitializeAsync (CampaignService campaign, CancellationToken token)
    {
        ScreenCatalog catalog = ScreenCatalog.Build(screens =>
        {
            screens.Register<CampaignMapRoute>(
                RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace,
                LowerPresentationPolicy.HideAndRetain,
                async (creation, cancellationToken) =>
                {
                    CampaignMapView view = await creation.AcquireScreenAsync(campaignPrefab, cancellationToken);
                    return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
                });
        });

        host = NavigationHost.Create(catalog);
        await host.StartAsync(new CampaignMapRoute(1), cancellationToken: token);
    }

    public async ValueTask ShutdownAsync ()
    {
        if (host is not null)
        {
            await host.ShutdownAsync();
        }
    }
}
```

The application calls initialization once, observes its exceptions, and calls shutdown before destroying the setup component or its dependencies. An `OnDestroy` callback alone cannot await that shutdown. `CampaignService` is supplied and owned by the application; it is not disposed by this factory.

`AcquireScreenAsync` validates and connects the prefab's presentation and registers destruction of the instance. It does not complete entry animation or start activity before returning to the factory. Those steps are part of the surrounding navigation operation.

The default single-instance policy is suitable for this chapter screen, which replaces its contents when showing another chapter. A popup that may appear more than once concurrently should use a `ScreenDefinition<TRoute, TResult>` with `ScreenInstancePolicy.Multiple`; each factory call must acquire a separate view. See the [confirmation popup registration](https://github.com/mackysoft/Navigathena/blob/main/tests/Unity/Assets/Samples/Common/Confirmation/ConfirmationPopupRegistration.cs).

## Load a scene and use its placed UI

For a scene enabled in the build's scene list, use `SceneAcquisition` from this package. Supply its full asset path, including the extension, to avoid ambiguous scene names:

```csharp
// Inside the screen factory.
CampaignMapView view = await creation.AcquireScreenAsync<CampaignMapView>(
    new SceneAcquisition("Assets/Scenes/Campaign.unity"), token);
return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
```

The scene is loaded additively; the active scene and unrelated scenes are left intact. Each acquisition owns its loaded instance, even when another instance of the same scene already exists. Built-in acquisitions serialize native loads while identifying each scene instance. Do not concurrently load the same path through an unmanaged loader. A cancelled native load is allowed to finish before the managed lifetime unloads it.

The same API also accepts other implementations of `IResourceAcquisition<Scene>`. For an Addressables scene, use `AddressablesSceneAcquisition`:

```csharp
using MackySoft.Navigathena.Unity.Addressables;

// Inside the screen factory; campaignScene is an Inspector AssetReference.
CampaignMapView view = await creation.AcquireScreenAsync<CampaignMapView>(
    new AddressablesSceneAcquisition(campaignScene), token);
return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
```

`AddressablesSceneAcquisition` loads the scene additively. `AcquireScreenAsync` uses the supplied acquisition, resolves the view, and connects its presentation. It does not choose how a scene is obtained: an `IResourceAcquisition<Scene>` can also create a scene at runtime. The default overload requires exactly one component of the requested type in the acquired scene. An overload accepting `Func<Scene, TView>` lets you select a view explicitly when the scene contains more than one. It verifies that the selected component belongs to that scene.

For DI, request the scene's installer component instead of its view, then pass the acquired installer to `creation.CreateScope`. The [startup sample](https://github.com/mackysoft/Navigathena/blob/main/tests/Unity/Assets/Samples/Startup/CampaignNavigationSetup.cs) does this with `CampaignMapInstaller` for manual construction, Microsoft.Extensions.DependencyInjection, and VContainer.

Do not unload an acquired scene yourself while a managed screen uses it. Its acquisition owns unloading, after dependent activity, lifecycle termination, and scope disposal have finished.

## Use a custom view acquisition

An acquisition that returns a configured component uses the same screen entry point:

```csharp
// viewAcquisition implements IResourceAcquisition<CampaignMapView>.
CampaignMapView view = await creation.AcquireScreenAsync(viewAcquisition, token);
return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
```

The acquisition decides how to obtain and release the view. `AcquireScreenAsync` does not clone it or load another scene. It observes the view's native lifetime and connects its `ScreenPresentation`. The runtime owns the acquisition before calling it, ends it after dependent screen work stops, and also ends partial acquisition after failure or cancellation. Use `Progress.GetReporter(input)` to report typed acquisition or release state. Native scene acquisition reports `SceneProgress` through `UnityProgress.SceneLoading` and `UnityProgress.SceneUnloading`: the scene path, the native operation fraction, and completion remain distinct from navigation completion.

Use `initialization.Lifetime` for supporting assets or objects acquired in `InitializeAsync(initialization, token)`. They share the screen instance's lifetime without reopening construction or creating another DI scope. Report game-specific generation steps through `initialization.Progress`. Configure an independent transition with `TransitionEndTiming.AfterResourceRelease` when the loading overlay must cover unloading as well as loading; activation and input always follow the effect's final settlement.

Use `BorrowScreenAsync` for an externally owned view whose owner must outlive the screen. Borrowing does not transfer destruction responsibility to the screen.

## Use a view already in a scene

The scene's owner creates a `ResourceLifetime` and a reference to its existing view. The screen borrows that view rather than loading or cloning it:

```csharp
ResourceLifetime sceneLifetime = new();
ResourceReference<CampaignMapView> viewReference = sceneLifetime.Reference(sceneView);

ScreenDefinition<CampaignMapRoute> screen = new(async (creation, token) =>
{
    CampaignMapView view = await creation.BorrowScreenAsync(
        viewReference, ScreenAnimationState.Hidden, token);
    return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
});
```

`sceneView` and `campaign` are supplied by the application owner. Register `screen` in the catalog as usual. The specified return state is applied when the screen's presentation connection ends. Choose it for the state the external owner expects; borrowing does not transfer ownership or unload the scene.

Before destroying that external scene or its view, await successful completion of `sceneLifetime.EndAsync()`. It stops registered users but does not destroy their borrowed values. If shutting down the whole navigation system, await `host.ShutdownAsync()` first, then end the external lifetime and release the scene.

`ConnectScreen` is a lower-level connection method for a view already acquired or borrowed through the creation lifetime. It does not acquire or destroy the object. Prefer the acquisition/borrowing helpers for ordinary screen factories.

## Configure push-in, push-out, pop-in, and pop-out

Assign a dedicated Animator to `AnimatorScreenAnimationDriver`. The default state paths are:

| Screen action | Animator state |
| --- | --- |
| Enter | `Base Layer.PushIn` |
| Cover by another screen | `Base Layer.PushOut` |
| Reveal when returning | `Base Layer.PopIn` |
| Exit | `Base Layer.PopOut` |

Also provide the immediate pose states `Base Layer.BeforeEnter`, `Base Layer.Foreground`, `Base Layer.Background`, `Base Layer.Hidden`, and `Base Layer.AfterExit`. Configure different full state paths in the Inspector if your controller uses other names.

Use finite, non-looping animation clips without automatic transitions. The driver waits for playback to finish, applies the final pose, and stops the Animator. It owns playback exclusively; another component must not write to the same Animator during navigation. The target must stay active until playback has stopped. The driver uses unscaled time and a configurable timeout.

Keep presentation gating separate from the animated content. Animate a visual child rather than disabling the screen's Canvas adapter or GameObject. For another animation library, implement `IScreenAnimator` on your own component and assign it in `ScreenPresentation`.

## Startup overlays and persistent objects

An overlay that must be visible before any loading belongs to the application's initial scene or another existing owner. Do not instantiate it as part of the first screen merely to cover that screen's loading.

The [startup example](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Startup) provides:

- `CampaignNavigationSetup`: Inspector references, catalog creation, and host lifetime.
- `StartupOverlay`: the existing overlay's status label, progress bar, and opacity control.
- `StartupProgress`: the typed composition of Addressables acquisition and campaign preparation state.
- `StartupRevealEffect`: the `INavigationTransitionEffect` that reveals the initial destination after preparation and resource release.

Place the overlay in the initial scene with its Canvas enabled, `CanvasViewAdapter.presentBeforeNavigation` enabled, and its visual `CanvasGroup.alpha` set to one. Assign the adapter, opacity group, `Text`, and `Slider` to `StartupOverlay` in the Inspector. Configure the overlay's Canvas to cover the starting screen before host initialization. Set `campaignScene` to the Addressables scene containing the configured `CampaignMapInstaller` and `ScreenPresentation`.

The setup borrows the overlay through an external resource lifetime and passes its typed transition to `host.StartAsync`. `ObserveProgress` connects `StartupOverlay.Render` during transition construction. `CampaignMapPresenter.PrepareAsync` reports the prepared chapter; it does not operate the overlay. The sample reports on Unity's main thread. After preparation and cleanup, `SettleAsync` fades out the overlay, then Navigathena activates the campaign screen and enables input. On failure the effect leaves the overlay opaque; the application's startup caller handles the exception and decides whether to retry or show an error.

Await `CampaignNavigationSetup.ShutdownAsync` before destroying the setup, overlay, or shared providers. The setup borrows but never destroys the preplaced overlay. It can live in a persistent scene or a persistent object; neither arrangement is required by Navigathena.

## Auxiliary resources

Use `creation.Lifetime.AcquireSceneAsync<TView>` or `InstantiatePrefabAsync<TView>` for a supporting scene or prefab whose resources belong to the screen but which is not another main screen presentation. These helpers acquire resources without automatically connecting a screen. Use `preparation.Lifetime` for resources specific to a route preparation.

Only create a separate screen or child region when it needs its own history, lifecycle, and presentation management. Ordinary labels, headers, and buttons can stay inside the parent view.

See also [uGUI configuration](https://mackysoft.github.io/Navigathena/src/MackySoft.Navigathena.Unity.UGUI/README.html), [Addressables acquisition](https://mackysoft.github.io/Navigathena/src/MackySoft.Navigathena.Unity.Addressables/README.html), and [the complete user guide](https://mackysoft.github.io/Navigathena/).
