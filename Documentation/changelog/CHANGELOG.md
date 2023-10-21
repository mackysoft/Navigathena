# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] - 2022-03-04

### Added
- Added `IPool.ReleaseInstancesPeriodically()` extension method.
- Added `IPool.BindTo()` extension method.
- Added Timer APIs

### Changed
- Extracted `IPool` from `IPool<T>`.
- `IPool<T>.Clear()` make to non-generic.

## [0.1.0] - 2022-03-01
First release
