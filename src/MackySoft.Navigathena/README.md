# MackySoft.Navigathena

An engine-independent screen navigation library. The runtime manages per-region history, screen instances, typed lifecycles, resource ownership, presentation, input, transitions, blockers, and recovery.

## Create a host

For a complete route, presenter, state capture, and setup example, start with [Getting started](https://mackysoft.github.io/Navigathena/#getting-started). The example below uses its application-defined `ChapterRoute`, `ChapterPresenter`, and view.

```csharp
using System.Threading.Tasks;
using MackySoft.Navigathena;
using MackySoft.Navigathena.Hosting;

ScreenCatalog catalog = ScreenCatalog.Build(screens =>
{
    screens.Register<ChapterRoute>(
        RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace,
        LowerPresentationPolicy.HideAndRetain,
        (creation, token) =>
        {
            return new ValueTask<IScreenLifecycleHandler<ChapterRoute>>(
                creation.Lifetime.CreateOwned(() => new ChapterPresenter(view)));
        });
});

await using var host = NavigationHost.Create(catalog);
await host.StartAsync(new ChapterRoute(1));
// Keep this scope alive while the application uses the host.
```

`ScreenCatalog.Build` combines root route definition and construction registration. Entry permissions and the lower-screen policy are required arguments, before the construction factory. The optional final callback defines child regions; it does not supply these required values. Building a catalog does not construct its screens: the factories run when navigation needs them.

The factory returns a single presenter as the screen's lifecycle entry point. Receive the route through the arguments to `PrepareAsync` and `ActivateAsync`, not through the presenter's constructor.

## Lifecycle and ownership

Use `creation.Lifetime` and `initialization.Lifetime` to register resources owned or borrowed by the screen instance, and `preparation.Lifetime` for resources used by the current preparation. Each exposes `LifetimeContext`, which provides `CreateOwned`, `AcquireAsync`, and `BorrowAsync`. Construction and initialization have separate registration windows but share instance ownership. Keeping the construction context does not extend its registration window. The runtime waits for dependent activity to stop before releasing resources. Do not register an object with `CreateOwned` if a DI container already owns it. This API is unrelated to Unity's `Resources.Load`.

`InitializeAsync(initialization, token)` runs once per instance; `PrepareAsync(route, preparation, token)` runs for the current history input. Both can report typed progress. `TerminateAsync(progress)` stops screen-specific work, then acquisitions receive `ReleaseAsync(progress)` for the ending operation. Normal owned objects and DI scopes retain their standard disposal contracts. Progress is observational: initialization and preparation must finish, then visual effects must finish, before `ActivateAsync` and input opening.

Activity and foreground availability have separate lifetimes. `ActivateAsync` starts activity before native input opens; it does not run again when a screen returns to the foreground if that activity continued while covered. Implement `IScreenForegroundLifecycleHandler` on the same presenter to receive `OnForegroundAvailable` after view permissions, blockers, and ordering are ready. Choose and set the initial or returning focus target using application-owned UI code there. `ScreenForegroundContext.CancellationToken` ends that foreground period independently of the activity token. Native admission closes before `OnForegroundUnavailable`; a later return supplies a new context. Failed publication, presentation loss, and shutdown also end foreground availability.

Owned objects and acquisitions share one registration sequence and release in reverse order. Register dependencies before their users. At screen termination, preparation lifetimes release before the instance lifetime; perform final operations using initialization or preparation resources in `TerminateAsync`, while they remain available. See [ownership and shutdown](https://mackysoft.github.io/Navigathena/#ownership-and-shutdown) for release failures and external owners.

`activity.StartWork` schedules a continuation owned by the screen's current history binding. It survives activity suspension; ending the binding cancels and awaits it before cleanup. `ScreenWork.WaitAsync` includes the entire callback and asynchronous `finally` blocks. Canceling that wait leaves execution running; `ScreenWork.Cancel` requests execution cancellation. A finished callback's exception remains observable without preventing cleanup. See [screen-owned asynchronous work](https://mackysoft.github.io/Navigathena/#screen-owned-asynchronous-work).

`ScreenDefinition` defaults to `Single`. New history entries for the same definition normally reuse the existing instance; history entries do not each require a separate view or DI scope. Choose `Multiple` when the factory can acquire and release independent instances.

Your application owns the host's lifetime. Resetting the root region does not shut down the host or external shared services. Before replacing the host itself, await successful completion of `ShutdownAsync`. External views and scenes borrowed by a screen have their own lifetimes, separate from the screen and host.

## History and presentation

Use `Reset` to return to a starting point, or `ReplaceFromAsync` to replace a specific history entry and the entries above it. Set `NavigationOptions.RecreateInstance` to recreate the destination and its initial child screens, including their screen scopes. `Reload` rebuilds screen instances while preserving the current route and child history; it does not create a new visit.

`LowerPresentationPolicy` applies to lower screens in the same region and their children. For example, place a HUD, menu, and editor screen in the same history, and give the editor `HideAndRetain` to hide the lower UI without destroying it. `Back` then returns to the menu. Parent and unrelated regions are not implicitly hidden. Shared blockers reuse one instance per definition across regions.

`Preserve` retains lower output and activity, while `SuspendActivity` retains output but ends lower activity periods. Both keep background native input closed. Output, activity, retention, and the input boundary are separate policy settings. `LowerPresentationBoundary.WhenLowerVisible` establishes a boundary over visible lower history; `Required` also establishes one without lower history, for example for a first popup in a child region. A registered or default blocker is used when lower output is preserved. Use distinct blocker definitions for simultaneously active independent regions.

## Child regions

Use separate definition and construction steps when a parent screen owns another navigation history. In this example, `ShellRoute` is an application route, and `shellScreen` and `chapterScreen` are already constructed `ScreenDefinition<ShellRoute>` and `ScreenDefinition<ChapterRoute>` values. The shell and chapter factories each return their own lifecycle handler and connect their own views.

```csharp
RegionDefinitionId rootId = new("game");
RegionDefinitionId contentId = new("content");

NavigationDefinition definition = NavigationDefinition.Build(
    rootId, RegionCompositionMode.Layered, root =>
    {
        root.AddRoute<ShellRoute>(
            RouteEntryOperations.Reset,
            LowerPresentationPolicy.HideAndRetain,
            shell =>
            {
                shell.AddChildRegion(contentId, RegionCompositionMode.Layered, RegionOccupancy.Required, content =>
                {
                    content.AddRoute<ChapterRoute>(
                        RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace,
                        LowerPresentationPolicy.HideAndRetain);
                });
            });
    });

ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
{
    builder.RegisterScreens(rootId, screens => screens.RegisterScreen(shellScreen));
    builder.RegisterScreens(contentId, screens => screens.RegisterScreen(chapterScreen));
});

await using var host = NavigationHost.Create(catalog);
await host.StartAsync(
    Destination.For(new ShellRoute())
        .Child(contentId, Destination.For(new ChapterRoute(1))));
```

The required child receives its initial route through `Destination.Child`. An optional child can start empty. A chapter's `activity.Navigation` changes the content history, not the shell history. The shell can explicitly address its child with `GetRegionNavigation(RegionTarget.Child(contentId))`.

If the child uses references obtained from the shell's scene, register its factories with `creation.RegisterScreens(contentId, ...)` inside the shell factory instead of registering those factories in the catalog. This makes the references available after loading. Do not register the same factory twice.

The parent owns its child regions. Removing the parent removes them; reusing a single parent instance for another visit ends the old child instances while retaining that visit's child history and captured state for return.

## Transition policy

Configure whole-transition effects separately from screen construction. This example assumes a catalog with `ChapterRoute` registered in its default `root` region. `fade` is an application-created `NavigationTransition` that acquires or borrows the overlay it needs.

```csharp
using System.Collections.Generic;

RegionNavigationOptions regionOptions = new()
{
    DefaultTransition = fade
};
regionOptions.Transitions.On(NavigationOperationKind.Push)
    .To<ChapterRoute>().Use(fade);
regionOptions.Transitions.On(NavigationOperationKind.Back)
    .From<ChapterRoute>().Use(NavigationTransition.None);

NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions
{
    Regions = new Dictionary<RegionDefinitionId, RegionNavigationOptions>
    {
        [new RegionDefinitionId("root")] = regionOptions
    }
});
```

Rules match exact route types and operation kinds. A rule specifying both `From` and `To` is more specific than a rule specifying only one. Conflicting equally specific rules are rejected; registration order does not resolve the conflict. Register policies before creating the host.

An operation's `NavigationOptions.Transition` overrides these rules. `null` uses policy; `NavigationTransition.None` explicitly omits the whole-transition effect. Neither choice removes the screen's own animation driver.

For an effect backed by the source or destination screen, the screen factory can connect it with `creation.SetTransitionEffect(effect, viewAdapter)` and the policy can select `NavigationTransition.FromSourceScreen(...)` or `FromDestinationScreen(...)`. Keep its dependencies in the screen lifetime. An operation-owned effect instead acquires them through `NavigationTransitionPreparationContext.Lifetime`.

Set `NavigationTransition.EndTiming` through the constructor's `endTiming` argument. The default `AfterPresentation` settles the effect before retired screens finish releasing. `AfterResourceRelease` waits for their termination and resource release, keeping the effect subscribed to progress, before settling and activating the destination. A source-owned effect cannot cover the release of its own screen; use an independent overlay for that case. See [loading progress](https://mackysoft.github.io/Navigathena/#loading-progress) for reporting and overlay configuration.

`RegionNavigationOptions.ResourcePolicy` chooses whether incoming preparation precedes outgoing release or vice versa. Use `OutgoingFirst` when a destination cannot acquire its resource while the outgoing screen owns it. An effect requiring simultaneous screens must declare `requiresSimultaneousScreens: true`; the runtime rejects plans that cannot satisfy that requirement.

## Navigation and waiting

Within a screen, use `ScreenActivityContext.Navigation`. From outside a screen, use `host.Client` with the target `RegionInstanceId`.

| API | Return value and completion |
| --- | --- |
| `Push`, `Replace`, `Reset`, `Back`, `Reload` | Submit a request and return a `NavigationOperation`. Returning from the call does not mean the transition has completed. |
| `PushAsync`, `ReplaceAsync`, `ResetAsync`, `BackAsync`, `ReloadAsync` | Extension methods that return a `Task` completing when the transition finishes. |
| `InvokeAsync(Route, …)` | Wait for the called screen to close and release its resources. Calls made from a screen also wait for the caller to resume activity. |
| `InvokeAsync<TResult>(Route<TResult>, …)` | Wait for the same cleanup, then return `TResult`. Closing without an answer cancels the call. |

Asynchronous navigation methods take the target, options, and `CancellationToken` in that order. Use a named argument to pass a token without specifying options.

```csharp
await navigation.PushAsync(route, cancellationToken: cancellationToken);

// answerRoute is an application-defined route derived from Route<TResult>.
var answer = await navigation.InvokeAsync(
    answerRoute,
    cancellationToken: cancellationToken);
```

Use `NavigationOperation` to observe or cancel an individual operation.

```csharp
NavigationOperation operation = navigation.Push(route);
NavigationResult completed = await operation.WaitAsync(waitCancellationToken);
```

The token passed to `WaitAsync` cancels only the wait. To request cancellation of the transition itself, call `operation.TryRequestCancellation()`. Tokens passed to methods such as `PushAsync` request transition cancellation and wait for cleanup to finish.

## Error handling

Both forms of waiting throw `NavigationException` for rejected, conflicting, or failed requests, and `OperationCanceledException` for cancellation. `RecoverAsync` also reports rejected, conflicting, or failed recovery requests through exceptions. You do not need to branch on `NavigationResult.Kind` or `DestinationCommitted` to determine success. Exceptions expose the commit and recovery state.

Invalid arguments or configuration cause argument exceptions or `NavigationConfigurationException` before a request is accepted.

`NavigationHostOptions.OperationCompleted` is an observation callback. It also reports rejected and conflicting requests processed by the runtime. Code that controls screens should await operation completion, not use this callback as a success signal.

## Platform support

Targets .NET Standard 2.1 and C# 9. Microsoft.Extensions.DependencyInjection and VContainer are optional adapters. Unity, uGUI, UI Toolkit, and Addressables adapters provide platform-specific resource acquisition and presentation.

- [Unity examples with and without dependency injection](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples)
