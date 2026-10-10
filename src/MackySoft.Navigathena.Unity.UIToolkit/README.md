# MackySoft.Navigathena.Unity.UIToolkit

Screen input permissions, native focus eligibility, pointer interception, and panel ordering for UI Toolkit.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Enable Unity's built-in UI Toolkit module. The package includes its Resources stylesheet and Unity metadata.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

See the [Unity installation instructions](https://github.com/mackysoft/Navigathena#unity-installation) and [Unity examples](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples).

## Configure and acquire a screen

Put `UiToolkitViewAdapter` and `ScreenPresentation` on the prefab root. Assign your UXML to **Content Asset** on `UiToolkitViewAdapter`, and add the adapter to **Views** on `ScreenPresentation`. Do not add `UIDocument` to the managed prefab: the host owns its native document and panel.

The application owner creates one `UiToolkitPresentationHost` for the navigation host. Supply a `PanelSettings` asset as a template. The host clones the settings for each view; it does not modify the shared asset or relocate the native theme. Managed views share a drawing-order domain but have separate native focus controllers.

```csharp
var ui = new UiToolkitPresentationHost(panelSettings);

// Inside a screen factory; screenPrefab is an Inspector-owned adapter reference.
IUiToolkitView view = await creation.AcquireUiToolkitScreenAsync(screenPrefab, ui, token);
return creation.Lifetime.CreateOwned(() => new MenuPresenter(view));
```

`AcquireUiToolkitScreenAsync` owns the instance and its panel. It builds a closed input boundary while the instance is inactive, activates the instance, explicitly attaches the host-owned document, and then connects its screen presentation. Initialization does not depend on application script execution order.

Use `view.ContentRoot.Q<Button>("confirm")` to obtain application controls. Screen implementations receive `IUiToolkitView`; they do not receive presentation permission setters. For an already acquired, active scene adapter, the composition code can call `ui.Connect(adapter)` and then `creation.ConnectScreen(adapter)`. The acquiring lifetime still owns that scene or instance.

Keep application controls within `ContentRoot`. The host owns the surrounding hierarchy and native objects; screen code does not configure or reconstruct the document. Disabling or destroying the adapter ends that connection; reenabling it does not restore input. Release all screen resources before disposing `ui` and its borrowed dependencies.

## Observe screen input permission

`TryCaptureInput` observes the currently permitted input period. `UiInputSession` exposes only its validity and revocation token; it does not operate individual controls or choose focus targets.

Capture one session when a user operation starts and retain it across awaits:

```csharp
Button confirm = view.ContentRoot.Q<Button>("confirm");

async Task RefreshAsync()
{
    if (!view.TryCaptureInput(out UiInputSession session))
    {
        return;
    }

    try
    {
        await LoadDataAsync(session.Revoked);
        if (!session.IsValid)
        {
            return;
        }

        // Updating and focusing this application control remain application work.
        confirm.Focus();
    }
    catch (OperationCanceledException) when (session.Revoked.IsCancellationRequested)
    {
        // This operation's input period ended.
    }
}
```

`IsValid` and `Revoked` describe the captured screen input permission. Closing and reopening a screen never revives an old session. The permission period is distinct from a control's availability and the screen's work lifetime: a retained screen can keep working while its input is blocked. Query UI state on the Unity player thread.

Controls within a blocked view cannot acquire native focus, including through direct `Focus()` calls or dynamically added controls. Navigation's pointer blockers intercept physical input without granting focus eligibility to their controls. The framework does not write application controls' enabled state, `focusable`, or `tabIndex`; game-defined availability and disabled styling remain the application's responsibility.

## Responsibility boundary

Navigathena determines whether a registered screen can receive input. The adapter enforces that permission within the managed content hierarchy and ends its owned focus and pointer captures when input closes. It does not infer ownership of UI attached elsewhere in the native panel.

The application and Unity's UI system determine initial focus, focus movement, control activation, dropdown behavior, and input delivery within permitted UI. The package exposes no individual-control focus operations. Configure and own the UI input backend in the application; no action or axis observer is required by this package. Native events are passed through or rejected within a closed scope, not buffered or redistributed to a different view.
