# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0-preview.3] - 2026-10-09

### Changed

- Changed Unity frame waits to prefer UniTask 2.1 or newer when installed, including on Unity 6. Unity 2023.1 and newer use the native Awaitable when UniTask is absent.

### Fixed

- Fixed Unity adapter compilation on Unity 2021.3 and newer for scene loading, prefab cleanup, Animator playback, and Addressables operations. UniTask 2.1 or newer is required on Unity versions before 2023.1. (#25)
- Fixed uGUI and UI Toolkit adapter compilation on Unity versions before 6000.4 by using object identities available in those editors.
- Fixed missing-dependency diagnostics on editors without Awaitable: compilation now reports that UniTask 2.1 or newer must be installed through Unity Package Manager.

## [2.0.0-preview.2] - 2026-10-07

### Fixed

- Fixed navigation requests, cancellation waits, recovery coordination, and shutdown stalling in Unity WebGL.
- Fixed competing native scene acquisitions waiting for managed worker threads in Unity WebGL.
- Fixed recovery of Single screen definitions after activation failures. Recovery now waits for the previous instance's resources to be released and preserves the committed history entry and route.

## [2.0.0-preview.1] - 2026-10-01

### Added

- Added an engine-independent .NET Standard 2.1 runtime with Task and ValueTask APIs, without Unity or UniTask dependencies in the common runtime.
- Added typed routes and an application-owned NavigationHost for screen lifecycles, history, state restoration, reload, and independently navigated child regions.
- Added result-returning screen calls and screen-owned asynchronous work that can continue across activity suspension.
- Added explicit ownership of acquired and borrowed screen resources, with awaited screen termination and host shutdown.
- Added separate Unity scene, prefab, uGUI, UI Toolkit, Addressables, and Animator integrations, plus Microsoft.Extensions.DependencyInjection and VContainer adapters.
- Added screen animations, whole-transition effects, and typed loading progress for application-specific loading displays.

### Changed

- **Breaking:** Replaced the 1.x scene-navigation and entry-point APIs with typed routes, screen factories, and lifecycle handlers. Existing screen implementations require migration, not just a package-reference update.
- **Breaking:** Changed distribution from the com.mackysoft.navigathena UPM package to seven NuGet packages. Remove the old UPM package and manually installed Navigathena files before installing 2.0; use NuGetForUnity for Unity and keep all Navigathena packages at the same version. Install adapter dependencies separately through Unity Package Manager.

### Removed

- Removed arbitrary history mutation, the interruptOperation parameter, and the OnEditorFirstPreInitialize hook. Use supported navigation commands, route preparation, and application startup code instead.

## [1.0.4] - 2023-11-07

### Changed

- `ScopedSceneEntryPoint` now ensures that `autoRun` set to disabled when `LifetimeScope` activation.
- Rename the `Create LifetimeScope` button to `Create Default LifetimeScope` in the `ScopedSceneEntryPoint` inspector.

## [1.0.3] - 2023-11-04

### Added

- Added `HistoryAssert`, an API for testing to ensure the correctness of the history.
- Added `ISceneEntryPointLifecycleAsserter`, an API for testing to ensure that the `ISceneEntryPoint` lifecycle history is correct.

### Changed

- `BlankSceneIdentifier` is now a test-only API.
- Rename from `BlankSceneIdentifier` to `AnonymousSceneIdentifier`.

### Fixed

- Fixed that scenes were not being cleaned up during test setup and that some tests were not functioning correctly.
- Fixed many compiler warnings.

## [1.0.2] - 2023-11-01

### Added

- Added `StandardSceneNavigatorTest`. This improves the stability of the features.

### Changed

- `BlankSceneIdentifier` make to generic.

### Fixed

- Fixed incorrect history on interrupt pop during pop.
- `OnExit` is no longer called if `OnEnter` was not called on a SceneEntryPoint when the transition process was canceled.

## [1.0.1] - 2023-10-31

### Added
- Add members into `ISceneHistoryBuilder`.
- Add `IContainerBuilder.RegisterSceneLifecycle` extension method.

## [1.0.0] - 2023-10-24

First release
