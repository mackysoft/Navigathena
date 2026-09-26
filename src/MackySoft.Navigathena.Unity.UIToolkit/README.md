# MackySoft.Navigathena.Unity.UIToolkit

UIDocument visibility, sorting, input adapters, and the required permission stylesheet.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Enable Unity's built-in UI Toolkit module. The package includes its Resources stylesheet and Unity metadata.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

See the [repository setup instructions](https://github.com/mackysoft/Navigathena#unity-への導入) and [Unity examples](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples).
