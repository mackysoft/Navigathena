# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
