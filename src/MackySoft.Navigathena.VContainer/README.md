# MackySoft.Navigathena.VContainer

Screen scope registration and ownership for VContainer.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Install VContainer from its official distribution first. The common Navigathena Runtime owns screen scope lifetime; this adapter does not implement another navigation host.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

See the [repository setup instructions](https://github.com/mackysoft/Navigathena#unity-への導入) and [Unity examples](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples).
