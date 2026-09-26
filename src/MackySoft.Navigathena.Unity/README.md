# MackySoft.Navigathena.Unity

Scene/Prefab acquisition, ScreenPresentation, and Animator integration.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Use Unity 6000.5.5f1, the version used by the package verification project.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

See the [repository setup instructions](https://github.com/mackysoft/Navigathena#unity-への導入) and [Unity examples](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples).
