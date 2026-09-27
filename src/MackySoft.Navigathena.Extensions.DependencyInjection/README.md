# Navigathena Microsoft.Extensions.DependencyInjection adapter

Return the result of `creation.CreateScope(services => …)` from the factory of `ScreenDefinition<TRoute>`.
Register the screen's single lifecycle entry point with `services.AddScreenLifecycleHandler<Presenter>()`. The container resolves its constructor dependencies through normal DI resolution.
A missing handler, multiple handlers, or a mismatched route type causes a configuration error before initialization.
Do not register the route in the container. Receive it through the arguments to `PrepareAsync` and `ActivateAsync`.

For screens that return a result, implement `IScreenLifecycleHandler<TRoute, TResult>`.
Factories registered through `ScreenCatalogDefinitionBuilder.Register<TRoute, TResult>` receive the same `ScreenCreationContext<TRoute, TResult>` as `ScreenDefinition<TRoute, TResult>`, so they can use `creation.CreateScope(services => …)` in the same way.

Each screen instance receives independent service registrations, a service provider, and an asynchronously disposable service scope.
`ImportService<T>(applicationProvider)` borrows an existing instance; closing the screen does not dispose of that service.
Dispose of the application provider only after the navigation host has shut down successfully.

Reusing a screen instance for another history entry does not rebuild its DI scope.
The runtime waits for lifecycle termination, awaits asynchronous scope disposal, and then releases the resources on which the scope depends.
