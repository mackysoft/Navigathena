# MackySoft.Navigathena.Unity.Addressables

Addressables acquisition and release of scenes and prefab assets.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Install Addressables from Unity Package Manager before importing this package.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

## Load a screen scene

Keep the `AssetReference` in application configuration, normally a serialized field on a Unity setup component. The screen's lifecycle handler does not load or unload its own scene.

```csharp
using MackySoft.Navigathena;
using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.Unity;
using MackySoft.Navigathena.Unity.Addressables;

// campaignScene and campaign are supplied by application setup.
ScreenDefinition<CampaignMapRoute> screen = new(async (creation, token) =>
{
    CampaignMapView view = await creation.AcquireScreenAsync<CampaignMapView>(
        new AddressablesSceneAcquisition(campaignScene), token);
    return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
});
```

The application types above are available in the [campaign example](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Campaign). Configure `ScreenPresentation` alongside the requested view as described in the [Unity guide](https://mackysoft.github.io/Navigathena/src/MackySoft.Navigathena.Unity/README.html).

`AddressablesSceneAcquisition` loads additively and activates the scene. When the screen no longer needs it, the runtime stops dependent users and awaits its managed unload. Do not separately unload or release the same handle.

## Instantiate a screen prefab

`AddressablesAssetAcquisition` acquires a prefab asset. The Unity helper then instantiates the configured screen and registers both lifetimes:

```csharp
CampaignMapView view = await creation.AcquireScreenAsync<CampaignMapView>(
    new AddressablesAssetAcquisition(campaignPrefab), token);
return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
```

Here `campaignPrefab` is the prefab's `AssetReference`. The prefab must have exactly one requested view component, on its root, with `ScreenPresentation` on that same object.

The created instance is destroyed before the acquired asset is released. Create a new acquisition object for each factory invocation; it stores the handles for that acquisition and must not be shared between independent screen instances.

## Cancellation and custom loading

Acquisitions report through `AddressablesProgress.Acquisition`, a `ProgressInput<AddressablesAcquisitionProgress>`. Each snapshot retains the asset GUID, the operation fraction, and `DownloadStatus` from Unity's ResourceManagement API. `OperationFraction` measures Addressables sub-operations, not bytes downloaded or the entire screen transition. Download completion therefore does not imply screen preparation has completed. `AddressablesProgress.Release` reports a nullable release fraction through the ending operation's reporter.

Use these inputs in a `ProgressDefinition<TState>` and pass the definition to `NavigationTransition.Create`. The factory receives the current operation's `ProgressSource<TState>`; connect its state to the loading view with `preparation.ObserveProgress(progress, view.Render)`. For concurrent loads, retain each `ProgressUpdate<T>.WorkId` separately in the display state. No progress receiver interfaces or per-navigation display wiring are required. Set `endTiming` to `TransitionEndTiming.AfterResourceRelease` to keep an independent overlay active until screen cleanup completes, and fade it out in `SettleAsync`. See [loading progress](https://mackysoft.github.io/Navigathena/#loading-progress) for the full typed connection.

Cancellation does not forcibly stop an Addressables operation. The acquisition waits for its result, checks cancellation, and lets managed cleanup release anything acquired. Do not release a still-used asset manually when a navigation token is cancelled.

For supporting objects that are not separate screen presentations, use `creation.Lifetime`, `initialization.Lifetime`, or `preparation.Lifetime` with `AcquireAsync` or the lower-level `InstantiatePrefabAsync` / `AcquireSceneAsync` helpers, according to their required lifetime. To support another asset system, implement `IResourceAcquisition<T>` and use the same common ownership contract. Do not acquire resources in its constructor; `ReleaseAsync(progress)` must also handle partial acquisition and report using the supplied release reporter.
