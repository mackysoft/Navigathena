# Navigathena 2.0

Navigathena is an engine-independent screen navigation library. It manages navigation history, screen lifecycles, presentation, input, screen calls, and screen-owned resources. Unity and dependency injection support are provided through adapters.

Use it to keep a title screen, gameplay HUD, menus, and popups consistent as screens open, close, reload, or return from history. Your application chooses the destinations and implements each screen's behavior. Navigathena coordinates when that behavior starts and stops, when views enter and leave, and when their resources can be released.

## Contents

- [Installation](#packages)
- [Getting started](#getting-started)
- [Screen lifecycle](#screen-lifecycle)
- [History, restoration, and reload](#history-restoration-and-reload)
- [Popups and results](#popups-and-results)
- [Regions and lower screens](#regions-and-lower-screens)
- [Unity scenes and prefabs](#unity-scenes-and-prefabs)
- [Transitions and progress](#transitions-and-progress)
- [Blockers](#blockers)
- [Dependency injection](#dependency-injection)
- [Ownership and shutdown](#ownership-and-shutdown)
- [Errors and cancellation](#errors-and-cancellation)
- [Upgrading from 1.x](#upgrading-from-1x)
- [FAQ](#faq)

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
2. Check the [Unity requirements](src/MackySoft.Navigathena.Unity/README.md#requirements), then install the dependencies required by your adapters through Unity Package Manager. Obtain uGUI, Addressables, and VContainer from their official distributions. UI Toolkit uses Unity's built-in module.
3. In NuGetForUnity, install `MackySoft.Navigathena.Unity` and the UI, Addressables, or dependency injection adapters you need. The core runtime is installed as a NuGet dependency. Keep all Navigathena packages at the same version.

Unity 2021.3 and newer are supported. UniTask is required before Unity 2023.1; on Unity 2023.1 and newer, it is optional. NuGet does not install UPM dependencies for you.

### .NET installation

Install `MackySoft.Navigathena` from NuGet. Add `MackySoft.Navigathena.Extensions.DependencyInjection` only if you use Microsoft.Extensions.DependencyInjection. The common runtime targets .NET Standard 2.1 and uses `Task` and `ValueTask`; it does not require Unity or UniTask.

## Getting started

A **Route** describes what to show and its input. A **screen definition** describes how to construct the screen. The factory returns exactly one **lifecycle handler**: your presenter, view model, or another application class implementing the lifecycle contract.

The following example displays chapters. `IChapterView` is an application-owned interface, not a Navigathena API. Its `Bind` implementation connects the UI to the supplied asynchronous callbacks, observes their completion and errors, and returns a subscription that removes those bindings. In Unity, use your own component or UI binding library for that implementation.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena;

public sealed record ChapterRoute : Route
{
    public ChapterRoute (int chapterId)
    {
        ChapterId = chapterId;
    }

    public int ChapterId { get; }
}

public interface IChapterView
{
    float ScrollPosition { get; set; }
    void ShowChapter (int chapterId);
    IDisposable Bind (Func<Task> next, Func<Task> back);
}

public sealed class ChapterPresenter : IScreenLifecycleHandler<ChapterRoute>, IScreenStateCapture
{
    private readonly IChapterView view;
    private IDisposable? bindings;

    public ChapterPresenter (IChapterView view)
    {
        this.view = view;
    }

    public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
    {
        return default;
    }

    public ValueTask PrepareAsync (
        ChapterRoute route,
        ScreenPreparationContext preparation,
        CancellationToken cancellationToken)
    {
        view.ShowChapter(route.ChapterId);
        view.ScrollPosition = preparation.SavedState is float saved ? saved : 0f;
        return default;
    }

    public ValueTask ActivateAsync (ChapterRoute route, ScreenActivityContext activity)
    {
        bindings = view.Bind(
            () => activity.Navigation.PushAsync(new ChapterRoute(route.ChapterId + 1)),
            () => activity.Navigation.BackAsync());
        return default;
    }

    public ValueTask DeactivateAsync ()
    {
        bindings?.Dispose();
        bindings = null;
        return default;
    }

    public ValueTask TerminateAsync (NavigationProgressReporter progress)
    {
        return default;
    }

    public object CaptureState ()
    {
        return view.ScrollPosition;
    }
}
```

Construct the catalog and host in application setup, not in each button handler:

```csharp
using System.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.Hosting;

public static class ChapterNavigation
{
    public static NavigationHost CreateHost (IChapterView view)
    {
        ScreenCatalog catalog = ScreenCatalog.Build(screens =>
        {
            screens.Register<ChapterRoute>(
                RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace,
                LowerPresentationPolicy.HideAndRetain,
                (creation, token) =>
                {
                    var presenter = creation.Lifetime.CreateOwned(() => new ChapterPresenter(view));
                    return new ValueTask<IScreenLifecycleHandler<ChapterRoute>>(presenter);
                });
        });

        return NavigationHost.Create(catalog);
    }
}
```

Call `await host.StartAsync(new ChapterRoute(1))` after creating the host. Keep the host for as long as this navigation system is needed, then `await host.ShutdownAsync()` before releasing the view or shared services.

This example connects screen logic only. To let Navigathena also manage physical visibility, input, sorting, and animation, acquire a configured view in the factory with the [Unity screen APIs](src/MackySoft.Navigathena.Unity/README.md). The lifecycle handler stays the same.

## Screen lifecycle

| Callback | What your implementation does |
| --- | --- |
| `InitializeAsync(initialization, token)` | Initialize stable dependencies once per screen instance. Acquire instance-owned resources through `initialization.Lifetime` and report through `initialization.Progress`. |
| `PrepareAsync(route, preparation, token)` | Load data for this route, populate the view, and restore `preparation.SavedState` before presentation. |
| `ActivateAsync(route, activity)` | Subscribe to input and start activity for the current visit. |
| `DeactivateAsync()` | Remove subscriptions and await the end of activity. Do not destroy the view or its dependencies here. |
| `TerminateAsync(progress)` | Finish remaining screen-specific work and report its progress before the owner releases resources. |

Route input belongs in `PrepareAsync` and `ActivateAsync`, not in the constructor or a DI registration. The same screen instance can display a different route value later.

A modal can stop a lower screen's activity while leaving its view visible. `DeactivateAsync` therefore does not mean `GameObject.SetActive(false)`, hiding the screen, or disposing its scope. When activity resumes, Navigathena passes a new activity context and the route belonging to that history entry. A retained screen resuming behind a closed modal normally does not need preparation again.

Use `activity.Reason`, `activity.PreparationReason`, and `activity.IsFirstActivation` when behavior depends on the reason for entry. Do not keep using an activity context after its activity has ended.

Navigathena checks cancellation before and after cancellable lifecycle callbacks. Forward the token to your asynchronous operations and cooperate with cancellation during their execution; you do not need to repeat an entry check solely for Navigathena. Stopping and termination are awaited even after cancellation.

For a new screen, initialization and route preparation finish before its entry animation and the transition effect finish. `ActivateAsync` runs after those visual steps, and native input opens only after activation completes. Progress reports, including a fraction of `1`, never advance this sequence; completion of the awaited callbacks does.

### Navigation from a lifecycle callback

Do not await a navigation operation inside the lifecycle callback that the current transition is waiting for. To redirect after successful activation, use the deferred methods:

```csharp
activity.Navigation.PostReplace(new ChapterRoute(2));
```

`PostPush`, `PostReplace`, `PostReset`, and `PostBack` run after activation succeeds. Normal UI callbacks can use and await the regular asynchronous navigation methods.

## History, restoration, and reload

From an active screen, use `activity.Navigation`. Application-level code uses `host.Client` and specifies the region, for example:

```csharp
await host.Client.PushAsync(host.Root, Destination.For(new ChapterRoute(2)));
await host.Client.BackAsync(host.Root);
```

| Operation | Meaning |
| --- | --- |
| `PushAsync(route)` | Add a visit to history and display it. |
| `BackAsync()` | Remove the current visit and return to the previous route and saved state. |
| `ReplaceAsync(route)` | Replace the current visit. |
| `ResetAsync(route)` | Replace the region's history with a new starting point. |
| `ReplaceFromAsync(target, route)` | Replace the selected visit and everything above it, including their child regions. |
| `ReloadAsync()` | Recreate the current screen and its child instances without adding a visit or changing the route. |

The corresponding methods without `Async` return a `NavigationOperation`; await `operation.WaitAsync()` to observe success or failure. `PushAsync` waits for opening, not for the screen to close.

### One instance is not one history entry

`ScreenDefinition` defaults to `ScreenInstancePolicy.Single`: one live instance for that shared definition within a host. Pushing chapter 1, then chapter 2, can use the same view, presenter, and DI scope. Navigathena saves chapter 1's state before preparing chapter 2. Back supplies chapter 1's route and saved state again.

Use `ScreenInstancePolicy.Multiple` when the factory can create and release independent instances, such as instantiated popup prefabs. A single scene-placed view cannot display two visits simultaneously. An incompatible simultaneous-use request is rejected; the library does not silently clone the view.

If one definition is registered in several regions, share the same `ScreenDefinition` object to preserve that single-instance contract. `Single` does not mean permanent: a released instance can be constructed again.

### State restoration and re-entry

Implement `IScreenStateCapture` on the lifecycle handler to capture an immutable value for a visit. Read it from `ScreenPreparationContext.SavedState` when that visit needs preparation. Navigathena does not automatically snapshot fields of your presenter or DI services.

Configure a definition's `HistoryReturn` for its usual return behavior, or override it for one Back operation:

```csharp
await navigation.BackAsync(new BackOptions
{
    Preparation = ScreenPreparationMode.Always,
    EnterAnimation = ScreenEnterAnimationMode.Always
});
```

This controls preparation and re-entry animation. It does not return an answer from another screen or recreate the instance.

Reload prepares the same route afresh by default. To restore captured display state on the new instance:

```csharp
await navigation.ReloadAsync(new ReloadOptions { RestoreState = true });
```

To restart an earlier screen from a deeper popup, select the history entry explicitly. `HistoryTarget.Unique<ChapterRoute>()` requires exactly one matching visit; missing or ambiguous matches are rejected. Use `HistoryTarget.Entry(entryId)` when several visits have the same route type.

```csharp
await navigation.ReplaceFromAsync(
    HistoryTarget.Unique<ChapterRoute>(),
    Destination.For(new ChapterRoute(1)),
    new NavigationOptions { RecreateInstance = true });
```

`RecreateInstance` also recreates initial child screens and their scopes. It cannot recreate an externally owned object that the screen only borrows.

## Popups and results

Use `Route` for an ordinary screen and `Route<TResult>` for a screen that must answer a caller. They are distinct contracts: a result route is opened with `InvokeAsync`, not ordinary `Push`.

The [confirmation popup example](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Common/Confirmation) defines `ConfirmationRoute : Route<bool>`, configurable title/message/button text, a view, a lifecycle handler, and prefab registration.

```csharp
bool accepted = await navigation.InvokeAsync(
    new ConfirmationRoute("Delete save?", "This cannot be undone.", "Delete", "Keep"),
    cancellationToken: cancellationToken);

if (accepted)
{
    // Perform the application's delete operation.
}
```

`ConfirmationRoute` in this snippet is the application type from that example, not a built-in popup. Register it once in the region where callers will open it.

The called presenter implements `IScreenLifecycleHandler<ConfirmationRoute, bool>`. Its activation receives `ScreenActivityContext<bool>`. The Accept button submits:

```csharp
activity.Call.Complete(true);
```

The Decline button instead submits `activity.Call.Complete(false)`, which is still a successful answer. A separate Close button can call `activity.Call.Dismiss()` to close without an answer.

Do not also call Back after `Complete`: it already requests the call's closure. Dismissal or closing the result screen through Back cancels the waiting task with `OperationCanceledException`; it is not the same as answering `false`.

`InvokeAsync<TResult>` returns after the call has closed, its resources have finished, and a surviving screen caller has resumed activity. Suspending the caller to display the popup does not itself cancel an accepted call. Use a cancellation token appropriate for the requesting operation, not the activity token merely because it is nearby. The application owns the surrounding business operation and its shutdown.

For a screen with no answer value, `await navigation.InvokeAsync(ordinaryRoute)` waits for its closure and caller resumption. Use `PushAsync` if you only need to wait for it to open. Do not await a user's eventual answer from `ActivateAsync`; use screen-owned work or start the request from an event or application operation after activation.

### Screen-owned asynchronous work

Use `activity.StartWork(async work => { ... })` for an operation that must keep using screen resources across a popup or activity suspension. The callback receives its own navigation and cancellation token. Its ownership belongs to the screen instance's current history binding, not to one activity period.

Work registered during `ActivateAsync` starts after activation succeeds and navigation settles. Failed activation cancels the work it registered without invoking it. Do not wait for owned work from a lifecycle callback or from that work itself.

`ScreenWork.WaitAsync()` waits for the entire callback, including code after `InvokeAsync` and asynchronous `finally` blocks. Its cancellation token cancels only the wait. `ScreenWork.Cancel()` requests execution cancellation; it does not force the callback to stop. Ending the screen binding cancels and awaits its work before releasing resources, so work that ignores cancellation keeps its dependencies alive.

Callback failures remain observable through `WaitAsync` and host diagnostics. A callback that finished with an exception no longer prevents screen termination and cleanup. After completion, the runtime releases the execution callback and cancellation resources; keeping the handle preserves the result for later waits.

## Regions and lower screens

A region is an independently navigated history, not a Canvas, panel, or scene. A simple HUD → menu → editor sequence can stay in one region. Use child regions when, for example, a persistent parent screen contains independently navigated content.

Each route specifies how it affects lower screens in its region:

| `LowerPresentationPolicy` | Lower screens |
| --- | --- |
| `Preserve` | Keep their output and input policy. |
| `BlockInput` | Remain visible, but no longer accept input or continue input-related activity. |
| `HideAndRetain` | Hide and stop activity; keep instances for returning. |
| `HideAndRelease` | Hide and release instances; keep history for reconstruction. |

For example, give the editor `HideAndRetain` to temporarily move the HUD and popups below it out of view; Back returns to the retained menu. The policy includes descendants of those lower screens, but not the owning parent or unrelated regions.

Use `NavigationDefinition.Build` and `route.AddChildRegion(...)` when defining a hierarchy. Register each region's factories through `ScreenCatalog.Build(definition, ...)`. Factories can also register child construction through `creation.RegisterScreens(...)` after acquiring a parent scene's references. See the [region example](src/MackySoft.Navigathena/README.md#child-regions).

Inside a screen, target another permitted region explicitly:

```csharp
IScreenNavigation rootNavigation =
    activity.Navigation.GetRegionNavigation(RegionTarget.Root);
await rootNavigation.ResetAsync(new ChapterRoute(1));
```

Calls establish navigation boundaries; a result screen cannot use a parent-region target to escape its call. Have application-level flow request an outer reset when it needs to replace the calling flow as well.

## Unity scenes and prefabs

Build a configured screen in the Inspector: your view component, `ScreenPresentation`, a UI adapter, and optionally an animation driver. The factory acquires this completed screen and constructs the presenter from the returned view.

```csharp
// Inside a ScreenDefinition factory. campaignPrefab is an Inspector reference.
CampaignMapView view = await creation.AcquireScreenAsync(campaignPrefab, token);
return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
```

For UI placed in a scene loaded during navigation:

```csharp
CampaignMapView view = await creation.AcquireScreenAsync<CampaignMapView>(
    new AddressablesSceneAcquisition(campaignScene), token);
return creation.Lifetime.CreateOwned(() => new CampaignMapPresenter(view, campaign));
```

These snippets use the application types from the [campaign example](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Campaign). `AcquireScreenAsync` is an extension method from `MackySoft.Navigathena.Unity`; `AddressablesSceneAcquisition` is in `MackySoft.Navigathena.Unity.Addressables`. The acquisition supplies the scene or view; the screen API connects its configured presentation and managed lifetime. Custom view acquisition uses the same entry point with `IResourceAcquisition<TView>`.

The scene is acquired before the presenter is constructed. The presenter can therefore receive references to objects placed in that scene. An already loaded scene uses borrowing instead of another load. A retained screen is not reloaded every time it becomes active.

- [Unity setup, scene-placed views, and animation](src/MackySoft.Navigathena.Unity/README.md)
- [uGUI view configuration](src/MackySoft.Navigathena.Unity.UGUI/README.md)
- [UI Toolkit adapter](src/MackySoft.Navigathena.Unity.UIToolkit/README.md)
- [Addressables scenes and prefabs](src/MackySoft.Navigathena.Unity.Addressables/README.md)

## Transitions and progress

There are two kinds of animation:

- **Screen animation:** enter, cover, reveal, and exit for an individual screen. Configure the driver on the screen prefab or scene object. The presenter does not play it manually.
- **Whole-transition effect:** a fade, loading overlay, or another effect spanning the switch. Implement `INavigationTransitionEffect` and configure a `NavigationTransition`.

`BeginAsync` starts the effect, `PrepareSwitchAsync` runs before the switch is committed, `AfterCommitAsync` performs post-commit animation, and `SettleAsync` establishes the final appearance on success or failure. The runtime awaits these callbacks; the effect does not update history, load destination scenes, or dispose of screens.

The [startup example](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples/Startup) borrows an overlay already present in the starting scene and fades it away only after the first screen is ready:

```csharp
await host.StartAsync(initialRoute, new NavigationOptions { Transition = startupReveal });
```

The application creates `startupReveal` as a `NavigationTransition`; it is not a special boot API. A root scene, `DontDestroyOnLoad` owner, or another application lifetime can own that overlay.

Register ordinary transition policy per region with `RegionNavigationOptions.Transitions.On(...).From<...>().To<...>().Use(...)`, or select one for a particular operation:

```csharp
await navigation.PushAsync(route, new NavigationOptions
{
    Transition = NavigationTransition.None
});
```

`None` omits the whole-transition effect, not the screen's own animation. The selection order is an explicit operation override, the most specific matching operation/from/to rule, the region default, then no effect. Equally specific rules selecting different effects are configuration errors. See the [transition policy example](src/MackySoft.Navigathena/README.md#transition-policy).

### Loading progress

Producers report their own typed data through `IProgress<T>`. A `ProgressInput<T>` identifies the input contract; a `ProgressDefinition<TState>` describes how the selected loading display combines those inputs. The transition receives a fresh `ProgressSource<TState>` for each operation. The producer does not depend on the display state or the loading view.

Define application-specific inputs once and pass reporters to ordinary services:

```csharp
public static class GameProgress
{
    public static readonly ProgressInput<WorldBuildState> WorldBuild =
        new("game.world-build");
}

// Inside PrepareAsync(route, preparation, cancellationToken):
await worldBuilder.BuildAsync(
    route.StageId,
    preparation.Progress.GetReporter(GameProgress.WorldBuild),
    cancellationToken);
```

`WorldBuildState`, `worldBuilder`, and `route.StageId` belong to the application. Its service accepts `IProgress<WorldBuildState>`; it does not need a Navigathena context. The state can retain domain-specific details such as generated regions, retry reasons, or participants. Navigathena does not require a fixed step-and-percentage representation.

For example, a loading display can combine Addressables acquisition data with world generation:

```csharp
public readonly struct LoadingState
{
    public LoadingState(
        AddressablesAcquisitionProgress? assets,
        WorldBuildState? world)
    {
        Assets = assets;
        World = world;
    }

    public AddressablesAcquisitionProgress? Assets { get; }
    public WorldBuildState? World { get; }
}

var definition = ProgressDefinition.Create(
        () => new LoadingState(null, null))
    .Reduce(AddressablesProgress.Acquisition,
        (state, update) => new LoadingState(update.Value, state.World))
    .Reduce(GameProgress.WorldBuild,
        (state, update) => new LoadingState(state.Assets, update.Value))
    .Build();
```

`AddressablesAcquisitionProgress` and `AddressablesProgress` belong to `MackySoft.Navigathena.Unity.Addressables`. They retain operation progress separately from download bytes. Native Unity scene loading uses `UnityProgress.SceneLoading` and `UnityProgress.SceneUnloading`, both carrying `SceneProgress`.

This example uses one acquisition at a time. For concurrent acquisitions, use `update.WorkId` to keep separate values in the display state instead of overwriting one field. Each `ProgressUpdate<T>` also identifies the operation, phase, history entry when applicable, and report sequence. Input identity is the `ProgressInput<T>` instance, not its diagnostic name.

For a display that directly consumes one input, use `ProgressDefinition.From(input, () => initialState)`. It needs no reducer, but rejects overlapping reporting callbacks for that input; use an explicit reduction to combine simultaneous work. All reported values, display states, and their referenced data must be immutable snapshots. Reducers must be pure and must not perform reporting or UI side effects.

Connect the display when acquiring the transition effect:

```csharp
var loading = NavigationTransition.Create(
    NavigationTransitionScope.Host,
    definition,
    async (preparation, progress, token) =>
    {
        var view = await preparation.Lifetime.BorrowAsync(
            overlayReference, token);

        preparation.RegisterExistingViewAdapter(view.NavigationView);
        preparation.ObserveProgress(progress, view.Render);

        return preparation.Lifetime.CreateOwned(
            () => new LoadingEffect(view));
    },
    endTiming: TransitionEndTiming.AfterResourceRelease);

await host.StartAsync(initialRoute,
    new NavigationOptions { Transition = loading });
```

`view.Render` accepts the application's `LoadingState`. `LoadingEffect` implements only `INavigationTransitionEffect`; progress does not require additional receiver interfaces. The runtime creates the source before acquiring the effect, immediately supplies the current snapshot when observing, serializes subsequent updates, and disconnects observers before releasing the display. `progress.Current` also exposes the latest snapshot, including the final snapshot after observation ends.

Report through the context for the work being performed:

| Work | Reporter |
| --- | --- |
| Screen construction | `creation.Progress` |
| Instance initialization | `initialization.Progress` |
| Route preparation | `preparation.Progress` |
| Resource acquisition | `ResourceAcquisitionContext.Progress` |
| Screen termination | `TerminateAsync(progress)` argument |
| Resource release | `ReleaseAsync(progress)` argument |

Call `GetReporter(input)` once for a service invocation. A reporter stops accepting samples when its owning callback finishes, including failure or cancellation. Use the supplied release reporter for release, rather than retaining the earlier acquisition reporter.

Show the overlay in `BeginAsync`, keep it visible in `AfterCommitAsync`, and fade it away in `SettleAsync`. With `AfterResourceRelease`, termination and resource release finish before settlement; destination activation and input follow settlement. A reported fraction of one never completes these callbacks. The effect also covers partial-acquisition cleanup after preparation fails.

This wait covers screens being ended and previous preparation resources whose use has finished. Resources still used by a retained call or screen work remain owned until those users finish; the transition does not wait for them to resume and complete. The default `AfterPresentation` settles the effect before releasing retired screens.

For an effect owned by a screen, connect its display definition using `creation.SetTransitionEffect(effect, adapter, definition, receive)`. An effect acquired from the destination cannot observe its earlier construction; use an independent overlay for that interval. An effect owned by a removed source screen cannot cover that screen's release with `AfterResourceRelease`; Navigathena rejects the configuration before stopping the screen.

A borrowed overlay's external owner must outlive the transition. Do not end its `ResourceLifetime` from a retiring screen's termination, DI disposal, or resource release: that would wait for an effect which is itself waiting for that screen to end.

Observers run synchronously on the reporting thread. For background producers, read `progress.Current` from the display's UI-thread update loop, or provide a UI-thread connection whose pending work ends before releasing the display. Observation does not select threads or run application jobs. Reducer and observer exceptions are reported by the navigation operation after safe settlement; they do not bypass cleanup.

`NavigationOptions.Progress` separately accepts `IProgress<NavigationProgress>` for runtime phase checkpoints. Its `PhaseFraction` may be absent and is not a whole-operation percentage. Host shutdown has no navigation-operation display source.

## Blockers

A blocker supplies the view between an input-blocking popup and the screens below it. It is not another history entry and does not replace lifecycle-based stopping of input subscriptions.

Set `NavigationHostOptions.DefaultBlocker` to a `BlockerDefinition` for a reusable default. A region's `RegisterBlocker<TRoute>(definition)` supplies a route-specific choice. Share the definition to share one blocker instance within a host, rather than registering a new instance for every region.

Implement `IBlockerPresenter` for its content and, optionally, `IBlockerAnimator` for decoration. `SetScreenContext` supplies the screen context currently associated with the blocker, allowing game-specific help text. Navigathena manages preparation, connection, sorting, animation completion, and termination. When a higher screen hides the lower content, the obsolete blocker is no longer shown.

## Dependency injection

DI changes construction, not the route or lifecycle contract. The same presenter can be created manually, resolved by Microsoft.Extensions.DependencyInjection, or resolved by VContainer.

| Construction | Factory returns |
| --- | --- |
| No DI | `creation.Lifetime.CreateOwned(() => new Presenter(view, service))` |
| Microsoft.Extensions.DependencyInjection | `creation.CreateScope(services => { ... })`, with `AddScreenLifecycleHandler<Presenter>()` |
| VContainer | `creation.CreateScope(parentResolver, installer)`, with `RegisterScreenLifecycleHandler<Presenter>()` in the installer |

The scope belongs to the screen instance, not each activation or history entry. Do not register the route in the container, dispose of injected dependencies from the presenter, or separately own a presenter already owned by its container.

- [Microsoft.Extensions.DependencyInjection setup](src/MackySoft.Navigathena.Extensions.DependencyInjection/README.md)
- [VContainer and Inspector-backed installers](src/MackySoft.Navigathena.VContainer/README.md)
- [One Unity setup with all three construction modes](https://github.com/mackysoft/Navigathena/blob/main/tests/Unity/Assets/Samples/Startup/CampaignNavigationSetup.cs)

## Ownership and shutdown

Use `creation.Lifetime` and `initialization.Lifetime` for resources belonging to an instance, and `preparation.Lifetime` for the current preparation's resources:

- `CreateOwned` constructs an object and registers its disposal with that lifetime.
- `AcquireAsync` executes an `IResourceAcquisition<T>` and registers cleanup, including partially failed acquisition.
- `BorrowAsync` uses an externally owned object through a `ResourceReference<T>` without taking responsibility for destroying it.

Construction and initialization have separate registration windows but share the same instance lifetime. A saved construction context stays closed during initialization; the initialization context closes when its callback returns. Initialization does not create another DI scope. Registration is not available during termination.

`CreateOwned` and `AcquireAsync` share one registration sequence within each lifetime and release in reverse registration order, regardless of when asynchronous acquisitions complete. Register dependencies before their users. A DI scope is one owned unit; the container controls disposal inside it. Borrowed objects remain externally owned.

When a screen ends, Navigathena closes input and work admission, stops activity and owned work, and waits for dependent users to stop. It then calls `TerminateAsync`, detaches the presentation, releases retained preparation lifetimes from newest to oldest, and releases the instance lifetime. Preparations from earlier re-entry are kept while work or calls still use them.

Use `TerminateAsync` for final operations that need resources acquired during initialization or preparation. An earlier-registered object's `Dispose` or `DisposeAsync` cannot depend on those later registrations still being alive. Normal disposal can use constructor dependencies registered before the object.

An acquisition's constructor must not start loading. Implement `AcquireAsync(context, token)` and `ReleaseAsync(progress)`; the runtime registers ownership before acquisition starts and awaits release when dependencies have stopped. Release must handle partial acquisition, including cancellation before acquisition starts. A failed release retains earlier registrations and borrowed references, and is not retried automatically. The [Addressables adapter](src/MackySoft.Navigathena.Unity.Addressables/README.md) is one such implementation. Ordinary owned services and DI scopes still use their normal `IDisposable` or `IAsyncDisposable` disposal contracts.

The application owns the host. Shutting it down stops screen activity and awaits screen termination and owned cleanup. Dispose of shared application containers and destroy borrowed objects only after the host and their registered users have stopped successfully:

```csharp
await host.ShutdownAsync();
await externalLifetime.EndAsync(); // If this owner supplied borrowed resources.
await applicationProvider.DisposeAsync(); // If this owner created a DI provider.
```

The last two objects are application-owned and only needed in the corresponding setup. Do not put their destruction in a `finally` that runs even when shutdown cannot finish safely. Shutdown failures throw; no success flag needs to be checked before proceeding. Concurrent and repeated shutdown calls join the same attempt, including a failed attempt. A shut-down host cannot be restarted; create a new one if the application is replacing the navigation system itself.

## Errors and cancellation

Await navigation completion to observe failures. Invalid arguments or configuration fail with argument exceptions or `NavigationConfigurationException`. Rejected, conflicting, or failed operations throw `NavigationException`; cancellation throws `OperationCanceledException`.

A failure after history was committed does not silently restore the old history. `NavigationException` exposes commit and recovery information. Use the host's state and recovery API to handle a screen requiring recovery instead of treating the old view as safe to reactivate.

Tokens passed to `PushAsync` and similar methods request transition cancellation and wait for cleanup. `operation.WaitAsync(token)` cancels only that wait; explicitly call `operation.TryRequestCancellation()` to request cancellation of the operation. Cancellation cannot undo an already committed destination.

`NavigationHostOptions.OperationCompleted` is for observation and diagnostics, including unsuccessful requests. It is not a substitute for awaiting a command's success.

## Upgrading from 1.x

Remove the old `com.mackysoft.navigathena` package and any manually installed Navigathena DLLs or source files before installing 2.0. Do not install the UPM and NuGet versions together: duplicate source files and asset GUIDs will conflict.

The setup and lifecycle APIs have changed; updating the package reference alone does not migrate screen implementations.

| 1.x concept | 2.0 usage |
| --- | --- |
| `GlobalSceneNavigator` / `ISceneNavigator` | Application-owned `NavigationHost`; screen-relative navigation from the activity context. |
| Scene identifier as destination | Route as input; a screen factory chooses and acquires the scene or prefab. |
| `ISceneEntryPoint` | One typed `IScreenLifecycleHandler` for each screen instance. |
| `Push` / `Pop` / `Change` | `PushAsync` / `BackAsync` / `ResetAsync`, or their operation-returning forms. |
| Scene data reader/writer | Typed route input and per-visit captured state. |
| `ITransitionDirector` | Screen animations and `INavigationTransitionEffect`, with separate selection policy. |
| `ScopedSceneEntryPoint` | A factory-created screen DI scope and an application installer. |

There is no direct replacement for arbitrary history mutation through `GetHistoryBuilderUnsafe`, the `interruptOperation` parameter, or `OnEditorFirstPreInitialize`. Use supported history commands for navigation, preparation for destination data loading, and application startup code for an Editor-specific starting route. Do not assume these are signature-only renames or that arbitrary insertion into the old transition sequence is supported.

The 1.x source and documentation remain available at the [1.1.0 tag](https://github.com/mackysoft/Navigathena/tree/1.1.0).

## FAQ

### Must the host live in a permanent root scene?

No. Its owner can be a root scene, a persistent object, a DI scope, or another application component. The owner must await shutdown before destroying the host's dependencies. Resetting the root history does not destroy the host or restart unrelated application services.

### Can a screen use several scenes or auxiliary prefabs?

Yes. Acquire related resources through the construction lifetime, and connect one main screen presentation. Auxiliary prefabs use the lower-level resource APIs; they are not automatically independent screens. Use separate screens or regions only when they require independent navigation and lifecycle.

### Can I use a view already placed in a loaded scene?

Yes. Its owner provides a resource reference, and the screen factory uses `BorrowScreenAsync`. Ending the screen disconnects it without unloading the owner's scene. See [existing scene views](src/MackySoft.Navigathena.Unity/README.md#use-a-view-already-in-a-scene).

### Can I enter a feature directly in the Unity Editor?

Have application startup choose that route and supply the same required services and view ownership as normal startup. If the feature's scene is already loaded, borrow its configured view instead of loading it again. There is no automatic replacement for the 1.x Editor pre-initialization hook.

### Does Reload restart my whole game?

No. It recreates the selected current screen and child instances. Use `ReplaceFromAsync` to restart a particular earlier visit, `ResetAsync` to return to a new root screen, or explicitly shut down and replace the host when restarting its owner and shared services.

## Further reading

- [Core runtime guide](src/MackySoft.Navigathena/README.md)
- [API reference](https://mackysoft.github.io/Navigathena/api/MackySoft.Navigathena.html)
- [Unity examples](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples)

## Help and contribute

Report bugs and request features through [issues](https://github.com/mackysoft/Navigathena/issues), or contribute a [pull request](https://github.com/mackysoft/Navigathena/pulls).

You can support development through [GitHub Sponsors](https://github.com/sponsors/mackysoft).

## Author

Created by Hiroya Aramaki ([Makihiro](https://twitter.com/makihiro_dev)).

## License

[MIT](LICENSE)
