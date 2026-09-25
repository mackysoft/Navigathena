# Navigathena VContainer

Screen scope registration and ownership for VContainer.

Install `MackySoft.Navigathena` at the same version through NuGetForUnity before importing this adapter. This UPM package does not contain the Runtime DLL or NuGet dependencies.

Install VContainer 1.19.0 through its official UPM installation instructions first. Its child scope is owned by the common Navigathena Runtime; this adapter does not implement a separate navigation host.

Use the packaged `.tgz` from the matching GitHub Release, or the repository Git URL with `?path=/packages/com.mackysoft.navigathena.vcontainer#<version-tag>`. Install the base Unity adapter before its UI or Addressables extensions. For the development branch, use `#2.0` only after that branch has been pushed.

See the repository README for setup and the Unity integration examples in `tests/Unity/Assets/Samples`.
