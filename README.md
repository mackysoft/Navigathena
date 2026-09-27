# Navigathena 2.0

Navigathena is an engine-independent screen navigation library. It manages navigation history, screen lifecycles, presentation, input, screen calls, and screen-owned resources. Unity and dependency injection support are provided through adapters.

## Packages

All packages are distributed through NuGet. In Unity, install them with [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).

| Package | Purpose | Contents |
| --- | --- | --- |
| `MackySoft.Navigathena` | Core runtime (required) | .NET Standard 2.1 DLL |
| `MackySoft.Navigathena.Extensions.DependencyInjection` | Microsoft.Extensions.DependencyInjection | .NET Standard 2.1 DLL |
| `MackySoft.Navigathena.Unity` | Scenes, prefabs, screen presentation, and Animator integration | Unity source and assets |
| `MackySoft.Navigathena.Unity.UGUI` | uGUI | Unity source and assets |
| `MackySoft.Navigathena.Unity.UIToolkit` | UI Toolkit | Unity source and assets |
| `MackySoft.Navigathena.Unity.Addressables` | Addressables | Unity source and assets |
| `MackySoft.Navigathena.VContainer` | VContainer | Unity source and assets |

Unity adapters include source files, assembly definitions, and asset metadata for Unity to compile. Navigathena 2.0 is not distributed as UPM packages.

## Unity installation

1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Install the dependencies required by your adapters through Unity Package Manager. Obtain uGUI, Addressables, and VContainer from their official distributions. UI Toolkit uses Unity's built-in module.
3. In NuGetForUnity, install `MackySoft.Navigathena.Unity` and the UI, Addressables, or dependency injection adapters you need. The core runtime is installed as a NuGet dependency. Keep all Navigathena packages at the same version.

Tested with Unity 6000.5.5f1 and NuGetForUnity 4.5.0. NuGet does not install UPM dependencies for you.

### Upgrading from 1.x

Remove the old `com.mackysoft.navigathena` package and any manually installed Navigathena DLLs or source files before installing 2.0. Do not install the UPM and NuGet versions together: duplicate source files and asset GUIDs will conflict.

The 1.x source and documentation remain available at the [1.1.0 tag](https://github.com/mackysoft/Navigathena/tree/1.1.0).

## Usage

- [Getting started](src/MackySoft.Navigathena/README.md)
- [API reference](https://mackysoft.github.io/Navigathena/api/MackySoft.Navigathena.html)
- [Unity examples](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples)

## License

[MIT](LICENSE)
