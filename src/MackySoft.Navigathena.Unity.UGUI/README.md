# MackySoft.Navigathena.Unity.UGUI

Canvas visibility, sorting, and input adapters.

Install this NuGet package through [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity). Keep all Navigathena packages at the same version. NuGet dependencies provide the common Runtime and, where required, the base Unity adapter.

Install uGUI from Unity Package Manager before importing this package.

This package contains Unity source files, assembly definitions, and asset metadata. NuGetForUnity restores them under the package's `Sources` directory for Unity to compile. It does not embed Unity, third-party assemblies, or another copy of the Navigathena Runtime. Do not install a Navigathena UPM package alongside it.

## Configure a Canvas screen

1. Use an independent screen-space root `Canvas` with `GraphicRaycaster`. World-space and nested Canvases are not supported by this adapter.
2. Add `CanvasViewAdapter` from `MackySoft.Navigathena.Unity.UGUI`.
3. Add the adapter to the `Views` list of the screen's `ScreenPresentation` component.
4. Leave `Present Before Navigation` off for a newly acquired screen. It starts with output and input closed until navigation is ready to present it.
5. Acquire the configured screen with `creation.AcquireScreenAsync` as shown in the [Unity guide](https://mackysoft.github.io/Navigathena/src/MackySoft.Navigathena.Unity/README.html).

The adapter controls `Canvas.enabled`, `GraphicRaycaster.enabled`, and sorting. It does not set `Selectable.interactable` or disable the screen GameObject. Buttons therefore do not need to change to a disabled visual style merely because another popup covers the screen.

## Foreground focus and navigation

Use the standard uGUI `EventSystem` and input module. Implement `IScreenForegroundLifecycleHandler` on the screen's presenter and set the initial or returning selection in `OnForegroundAvailable`, after Navigathena has opened the foreground view and published blockers and ordering:

```csharp
private ScreenForegroundToken foregroundToken;

public void OnForegroundAvailable (ScreenForegroundToken foreground)
{
    foregroundToken = foreground;
    eventSystem.SetSelectedGameObject(view.Decline.gameObject);
}
```

The application supplies the screen's `EventSystem` to its presenter and chooses the target. The [confirmation popup sample](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Common/Confirmation) selects **Decline** on both initial entry and return. It does not need an `OnForegroundUnavailable` callback. Navigathena does not choose a default button or restore a previously selected control.

Set each control's Navigation mode to **Explicit** and keep all four directional links within the same screen, or leave a direction unset. Apply the same rule to controls added at runtime. Explicit navigation follows those links without searching other screens, so keyboard and gamepad movement stays within the foreground screen. Automatic navigation searches uGUI's global selectable set and is not isolated by this adapter.

## Pointer input

Navigathena closes a covered view's `GraphicRaycaster` and clears its existing selection in the current uGUI `EventSystem` when view permissions close. This happens during presentation publication, without a selection-monitoring loop or an execution-order requirement. Configure the navigation host's blocker to receive pointer input behind the popup, including outside its visible content. Blockers must not be navigation targets.

The raycaster gate prevents new pointer hits on the covered view. It does not cancel a press or drag that an input module already holds. Do not treat it as cancellation of an in-progress pointer gesture.

Keep the adapter and its GameObject enabled while they are registered; disabling or destroying them is reported as a lost view, not an ordinary hide operation.

## Application operations

Keep screen activity subscriptions in `ActivateAsync` and remove them in `DeactivateAsync`. Activity can continue while covered, so an activity subscription alone does not mean the screen may execute a user operation. The confirmation popup keeps the `ScreenForegroundToken` supplied by `OnForegroundAvailable`. Its field starts with the invalid `default` value before the first notification, and a token becomes invalid when its foreground period ends. Its Accept listener, registered in `ActivateAsync`, checks that eligibility at the application's operation entry point:

```csharp
accept = () =>
{
    if (foregroundToken.IsValid)
    {
        activity.Call.Complete(true);
    }
};
view.Accept.onClick.AddListener(accept);
```

The sample applies the same check to Decline and Close. Navigathena supplies the foreground eligibility token; the application decides whether to execute its result handling or other game logic. A returning screen receives a fresh token without resubscribing if its activity continued. The old token remains invalid, so an end callback is not needed just to clear this field.

For asynchronous game work, capture `foregroundToken` when the operation starts. Before applying a result after an await, check the captured token's `IsValid`, not the presenter's latest token. A return to the foreground must not authorize work from an expired period. Pass the token's `CancellationToken` to work that supports cancellation. See [lifecycle and ownership](https://mackysoft.github.io/Navigathena/src/MackySoft.Navigathena/README.html#lifecycle-and-ownership).

This application-level check does not cancel native pointer state or suppress uGUI's own control updates. The raycaster gate, explicit navigation, and application operation checks have separate responsibilities.

## Sorting and animation

Navigathena adds the view's managed order to `Base Sorting Order`. Views that must participate in one managed ordering domain need matching render mode, target display, sorting layer, world camera, and base sorting order. Unrelated Canvases remain the application's responsibility; do not write to a managed Canvas's sorting order concurrently.

Use a separate visual child for scale, position, or opacity animation. Configure an `AnimatorScreenAnimationDriver` or another `IScreenAnimator` on `ScreenPresentation`; do not toggle the Canvas adapter from animation clips. See [screen animation setup](https://mackysoft.github.io/Navigathena/src/MackySoft.Navigathena.Unity/README.html#configure-push-in-push-out-pop-in-and-pop-out).
