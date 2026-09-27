# MackySoft.Navigathena

An engine-independent screen navigation library. The runtime manages per-region history, screen instances, typed lifecycles, resource ownership, presentation, input, transitions, blockers, and recovery.

## Create a host

```csharp
var screen = new ScreenDefinition<TitleRoute>((creation, token) =>
{
    return new ValueTask<IScreenLifecycleHandler<TitleRoute>>(
        creation.Lifetime.CreateOwned(() => new TitlePresenter(titleService)));
});

var catalog = ScreenCatalog.Build(definition, builder =>
    builder.RegisterScreens(rootRegion, screens => screens.RegisterScreen(screen)));

await using var host = NavigationHost.Create(catalog);
await host.StartAsync(new TitleRoute());
```

`TitleRoute`, `TitlePresenter`, and `titleService` are application-defined types and dependencies. The factory returns a single presenter as the screen's lifecycle entry point. Implement `IScreenLifecycleHandler<TitleRoute>` and receive the route through the arguments to `PrepareAsync` and `ActivateAsync`, not through the presenter's constructor.

## Lifecycle and ownership

Use `creation.Lifetime` to register resources owned or borrowed by the screen instance, and `preparation.Lifetime` for resources used by the current preparation. Both expose `LifetimeContext`, which provides `CreateOwned`, `AcquireAsync`, and `BorrowAsync`. The runtime waits for dependent activity to stop before releasing resources. Do not register an object with `CreateOwned` if a DI container already owns it. This API is unrelated to Unity's `Resources.Load`.

`ScreenDefinition` defaults to `Single`. New history entries for the same definition normally reuse the existing instance; history entries do not each require a separate view or DI scope. Choose `Multiple` when the factory can acquire and release independent instances.

Your application owns the host's lifetime. Resetting the root region does not shut down the host or external shared services. Before replacing the host itself, await successful completion of `ShutdownAsync`. External views and scenes borrowed by a screen have their own lifetimes, separate from the screen and host.

## History and presentation

Use `Reset` to return to a starting point, or `ReplaceFromAsync` to replace a specific history entry and the entries above it. Set `NavigationOptions.RecreateInstance` to recreate the destination and its initial child screens, including their screen scopes. `Reload` rebuilds screen instances while preserving the current route and child history; it does not create a new visit.

`LowerPresentationPolicy` applies to lower screens in the same region and their children. For example, place a HUD, menu, and editor screen in the same history, and give the editor `HideAndRetain` to hide the lower UI without destroying it. `Back` then returns to the menu. Parent and unrelated regions are not implicitly hidden. Shared blockers reuse one instance per definition across regions.

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
