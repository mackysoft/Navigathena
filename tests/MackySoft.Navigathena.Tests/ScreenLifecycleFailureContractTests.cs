using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenLifecycleFailureContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private sealed record InputRoute (int Value) : Route;

    [Fact]
    public async Task Single_activation_failure_recovers_the_committed_entry_after_releasing_its_old_instance ()
    {
        List<string> events = new();
        bool fail = false;
        int generations = 0;
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            if (generations > 0)
            {
                Assert.Contains("screen0.dispose", events);
            }
            Handler handler = creation.Lifetime.CreateOwned(() => new Handler("screen" + generations++, events)
            {
                Activate = _ => fail ? throw new InvalidOperationException("Activation failed.") : default
            });
            return new(handler);
        }));
        await host.StartAsync(new InputRoute(1));
        fail = true;
        NavigationException activation = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new InputRoute(2))));
        Assert.True(activation.DestinationCommitted);
        Assert.Equal(NavigationPresentationStatus.RecoveryRequired, activation.PresentationStatus);
        NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries.Single();
        PresentationState lost = host.State.Current.GetPresentation(entry);
        Assert.Equal(PresentationMaterialization.Lost, lost.Materialization);
        fail = false;

        NavigationResult recovery = await host.Recovery.RecoverAsync(lost.IncidentId!.Value);

        Assert.Equal(NavigationPresentationStatus.Ready, recovery.PresentationStatus);
        Assert.Equal(entry, host.State.Current.GetRegion(host.Root).Entries.Single());
        Assert.Equal(new InputRoute(2), host.State.Current.GetEntry(entry).Route);
        Assert.Equal(PresentationMaterialization.Available, host.State.Current.GetPresentation(entry).Materialization);
        Assert.Contains("screen1.activate.2", events);
    }

    [Fact]
    public async Task Single_recovery_waits_for_old_termination_before_acquiring_a_replacement ()
    {
        List<string> events = new();
        TaskCompletionSource<bool> terminating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool fail = false;
        int generations = 0;
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            int generation = generations++;
            return new(creation.Lifetime.CreateOwned(() => new Handler("screen" + generation, events)
            {
                Activate = _ => fail ? throw new InvalidOperationException("Activation failed.") : default,
                Terminate = async () =>
                {
                    if (generation == 0)
                    {
                        terminating.TrySetResult(true);
                        await release.Task;
                    }
                }
            }));
        }));
        await host.StartAsync(new InputRoute(1));
        fail = true;
        await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new InputRoute(2))));
        NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries.Single();
        fail = false;
        Task<NavigationResult> recovery = host.Recovery.RecoverAsync(host.State.Current.GetPresentation(entry).IncidentId!.Value).AsTask();
        try
        {
            Task first = await Task.WhenAny(terminating.Task, recovery).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(terminating.Task, first);
            Assert.False(recovery.IsCompleted);
            Assert.Equal(1, generations);
            Assert.DoesNotContain("screen0.dispose", events);
        }
        finally
        {
            release.TrySetResult(true);
        }

        NavigationResult restored = await recovery.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(NavigationPresentationStatus.Ready, restored.PresentationStatus);
        Assert.Equal(entry, host.State.Current.GetRegion(host.Root).Entries.Single());
        Assert.True(events.IndexOf("screen0.dispose") < events.IndexOf("screen1.initialize"));
    }

    [Fact]
    public async Task Single_recovery_preserves_the_lost_entry_when_old_termination_fails ()
    {
        List<string> events = new();
        bool fail = false;
        int generations = 0;
        NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            generations++;
            return new(creation.Lifetime.CreateOwned(() => new Handler("screen", events)
            {
                Activate = _ => fail ? throw new InvalidOperationException("Activation failed.") : default,
                Terminate = () => throw new InvalidOperationException("Termination failed.")
            }));
        }));
        await host.StartAsync(new InputRoute(1));
        fail = true;
        await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new InputRoute(2))));
        NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries.Single();
        PresentationState lost = host.State.Current.GetPresentation(entry);
        fail = false;

        NavigationException recovery = await Assert.ThrowsAsync<NavigationException>(async () => await host.Recovery.RecoverAsync(lost.IncidentId!.Value));

        Assert.False(recovery.DestinationCommitted);
        Assert.Contains("Termination failed.", recovery.ToString());
        Assert.Equal(1, generations);
        Assert.Equal(entry, host.State.Current.GetRegion(host.Root).Entries.Single());
        Assert.Equal(new InputRoute(2), host.State.Current.GetEntry(entry).Route);
        Assert.Equal(lost, host.State.Current.GetPresentation(entry));
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_and_activation_failures_preserve_their_respective_commit_boundary (bool failDuringActivation)
    {
        List<string> events = new();
        List<Handler> handlers = new();
        List<ThrowingView> views = new();
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            Handler handler = creation.Lifetime.CreateOwned(() => new Handler("screen" + handlers.Count, events)
            {
                Prepare = route => !failDuringActivation && route.Value == 2 ? throw new InvalidOperationException("Preparation failed.") : default,
                Activate = route => failDuringActivation && route.Value == 2 ? throw new InvalidOperationException("Activation failed.") : default
            });
            ThrowingView view = new();
            handlers.Add(handler);
            views.Add(view);
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { view }));
            return new(handler);
        }, ScreenInstancePolicy.Multiple));
        Assert.True((await host.Start(new InputRoute(1)).WaitAsync()).DestinationCommitted);
        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await handlers[0].Activity!.Navigation.PushAsync(new InputRoute(2)));
        Assert.Equal(failDuringActivation, result.DestinationCommitted);
        Assert.Equal(failDuringActivation ? 2 : 1, host.State.Current.GetRegion(host.Root).Entries.Count);
        Assert.False(views[1].Presentation.InputEnabled);
        if (failDuringActivation)
        {
            Assert.Equal(NavigationPresentationStatus.RecoveryRequired, result.PresentationStatus);
            Assert.False(views[0].Presentation.InputEnabled);
            Assert.True(handlers[1].Activity!.CancellationToken.IsCancellationRequested);
            Assert.Contains("screen1.deactivate", events);
            NavigationEntryId destination = host.State.Current.GetRegion(host.Root).Entries.Last();
            Assert.Equal(2, ((InputRoute)host.State.Current.Entries[destination].Route).Value);
        }
        else
        {
            Assert.True(views[0].Presentation.InputEnabled);
            Assert.DoesNotContain("screen1.activate.2", events);
            Assert.Contains("screen1.terminate", events);
        }
        await host.ShutdownAsync();
        Assert.Equal(1, events.Count(item => item == "screen0.dispose"));
        Assert.Equal(1, events.Count(item => item == "screen1.dispose"));
    }

    [Fact]
    public async Task Only_the_returned_handler_receives_lifecycle_calls_while_owned_collaborators_are_disposed ()
    {
        List<string> events = new();
        Handler first = new("first", events);
        Handler second = new("second", events);
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            creation.Lifetime.CreateOwned(() => first);
            return new(creation.Lifetime.CreateOwned(() => second));
        }));
        await host.StartAsync(new InputRoute(1));
        await host.ShutdownAsync();
        Assert.Equal(new[] { "second.initialize", "second.prepare.1", "second.activate.1", "second.deactivate", "second.terminate", "second.dispose", "first.dispose" }, events);
    }

    [Fact]
    public async Task Native_input_failure_does_not_skip_activity_stop_and_final_termination ()
    {
        List<string> events = new();
        Handler handler = new("screen", events);
        ThrowingView view = new();
        NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            creation.ConnectPresentation(new ScreenPresentationBinding(new[] { view }));
            return new(creation.Lifetime.CreateOwned(() => handler));
        }));
        await host.StartAsync(new InputRoute(1));
        view.FailNextClose = true;
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask());
        Assert.Contains("screen.deactivate", events);
        Assert.Contains("screen.terminate", events);
        Assert.Equal(1, events.Count(item => item == "screen.dispose"));
        Assert.True(handler.Activity!.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Acquired_handler_is_disposed_by_its_acquisition_exactly_once ()
    {
        List<string> events = new();
        Handler handler = new("screen", events);
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>(async (creation, token) =>
        {
            Handler acquired = await creation.Lifetime.AcquireAsync(new HandlerAcquisition(handler), token);
            return acquired;
        }));
        await host.StartAsync(new InputRoute(1));
        await host.ShutdownAsync();
        Assert.Equal(1, events.Count(item => item == "screen.dispose"));
        Assert.True(events.IndexOf("screen.terminate") < events.IndexOf("screen.dispose"));
    }

    [Fact]
    public async Task Cancellation_is_closed_before_mutating_a_reused_instance ()
    {
        List<string> events = new();
        TaskCompletionSource<bool> mutating = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Handler handler = new("screen", events)
        {
            Prepare = async route =>
{
    if (route.Value == 2)
    {
        mutating.SetResult(true);
        await proceed.Task;
    }
}
        };
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
{
    return new(handler);
}));
        await host.StartAsync(new InputRoute(1));
        NavigationOperation operation = handler.Activity!.Navigation.Push(new InputRoute(2));
        await mutating.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.TryRequestCancellation());
        proceed.SetResult(true);
        Assert.True((await operation.WaitAsync()).DestinationCommitted);
        Assert.Equal(2, host.State.Current.GetRegion(host.Root).Entries.Count);
    }

    [Fact]
    public async Task Missing_handler_fails_before_commit_and_releases_partial_construction ()
    {
        List<string> events = new();
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((creation, _) =>
        {
            creation.Lifetime.CreateOwned(() => new Handler("dependency", events));
            return default;
        }));

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(async () => await host.StartAsync(new InputRoute(1)));
        Assert.IsType<NavigationConfigurationException>(failure.InnerException);
        Assert.False(failure.DestinationCommitted);

        Assert.Empty(host.State.Current.Entries);
        Assert.Equal(new[] { "dependency.dispose" }, events);
    }

    [Fact]
    public async Task Sharing_a_live_handler_fails_without_initializing_or_terminating_it_again ()
    {
        List<string> events = new();
        Handler handler = new("shared", events);
        await using NavigationHost host = Create(new ScreenDefinition<InputRoute>((_, _) => new(handler), ScreenInstancePolicy.Multiple));
        await host.StartAsync(new InputRoute(1));

        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(async () => await handler.Activity!.Navigation.PushAsync(new InputRoute(2)));
        Assert.IsType<NavigationConfigurationException>(failure.InnerException);
        Assert.False(failure.DestinationCommitted);

        Assert.Single(host.State.Current.Entries);
        Assert.Equal(1, events.Count(item => item == "shared.initialize"));
        Assert.DoesNotContain("shared.prepare.2", events);
        Assert.DoesNotContain("shared.terminate", events);
        Assert.False(handler.Activity!.CancellationToken.IsCancellationRequested);
        await host.ShutdownAsync();
        Assert.Equal(1, events.Count(item => item == "shared.terminate"));
        Assert.DoesNotContain("shared.dispose", events);
    }

    private static NavigationHost Create (ScreenDefinition<InputRoute> screen)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<InputRoute>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.HideAndRetain));
        return NavigationHost.Create(ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(Root, screens => screens.RegisterScreen(screen))));
    }

    private sealed class Handler : IScreenLifecycleHandler<InputRoute>, IAsyncDisposable
    {
        private readonly string name;
        private readonly List<string> events;
        public Handler (string name, List<string> events)
        {
            this.name = name;
            this.events = events;
        }
        public ScreenActivityContext? Activity { get; private set; }
        public Func<InputRoute, ValueTask>? Prepare { get; init; }
        public Func<InputRoute, ValueTask>? Activate { get; init; }
        public Func<ValueTask>? Terminate { get; init; }
        public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
        {
            events.Add(name + ".initialize");
            return default;
        }
        public async ValueTask PrepareAsync (InputRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            events.Add(name + ".prepare." + route.Value);
            if (Prepare is not null)
            {
                await Prepare(route);
            }
        }
        public async ValueTask ActivateAsync (InputRoute route, ScreenActivityContext activity)
        {
            Activity = activity;
            events.Add(name + ".activate." + route.Value);
            if (Activate is not null)
            {
                await Activate(route);
            }
        }
        public ValueTask DeactivateAsync ()
        {
            events.Add(name + ".deactivate");
            return default;
        }
        public async ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            events.Add(name + ".terminate");
            if (Terminate is not null)
            {
                await Terminate();
            }
        }
        public ValueTask DisposeAsync ()
        {
            events.Add(name + ".dispose");
            return default;
        }
    }

    private sealed class HandlerAcquisition : IResourceAcquisition<Handler>
    {
        private readonly Handler handler;
        public HandlerAcquisition (Handler handler) => this.handler = handler;
        public ValueTask<Handler> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken) => new(handler);
        public ValueTask ReleaseAsync (NavigationProgressReporter progress) => handler.DisposeAsync();
    }

    private sealed class ThrowingView : IViewAdapter
    {
        public object Identity => this;
        public object OrderingDomain => typeof(ThrowingView);
        public bool IsAlive => true;
        public bool FailNextClose { get; set; }
        public ViewPresentation Presentation { get; private set; } = new(false, false, 0);
        public event Action<string>? Lost
        {
            add
            {
            }
            remove
            {
            }
        }
        public void Validate (ViewPresentation presentation)
        {
        }
        public void Apply (ViewPresentation presentation)
        {
            if (!presentation.InputEnabled && FailNextClose)
            {
                FailNextClose = false;
                throw new InvalidOperationException("Native input write failed.");
            }
            Presentation = presentation;
        }
    }
}
