#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Integration;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.Verification
{
    public sealed class WebGLNavigationVerification : MonoBehaviour
    {
        private readonly List<string> violations = new();
        private string stage = "starting";
        private string result = "RUNNING";
        private bool finished;
        private float started;

        private async void Start ()
        {
            started = Time.realtimeSinceStartup;
            try
            {
                await VerifyAsync();
                Report("PASS: initial navigation, retained navigation, cancellation, Single recovery, contended lifetime, shutdown");
            }
            catch (Exception exception)
            {
                Report("FAIL at " + stage + ": " + exception);
                Debug.LogException(exception);
            }
        }

        private void Update ()
        {
            if (!finished && Time.realtimeSinceStartup - started > 45)
            {
                Report("FAIL: timed out at " + stage);
            }
        }

        private async Task VerifyAsync ()
        {
            bool failActivation = false;
            List<Handler> handlers = new();
            ScreenCatalog catalog = ScreenCatalog.Build(screens => screens.Register<ProbeRoute>(
                RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, (creation, _) =>
                {
                    Require(handlers.All(handler => handler.Disposed), "A previous Single instance still owns its resources.");
                    Handler handler = creation.Lifetime.CreateOwned(() => new Handler(() => failActivation, violations));
                    handlers.Add(handler);
                    creation.ConnectPresentation(new ScreenPresentationBinding(new[] { handler.View }));
                    return new ValueTask<IScreenLifecycleHandler<ProbeRoute>>(handler);
                }));
            await using NavigationHost host = NavigationHost.Create(catalog);
            stage = "initial navigation";
            await host.StartAsync(new ProbeRoute(1));
            Require(handlers.Count == 1, "Initial navigation did not acquire its screen.");

            stage = "retained navigation";
            Task<NavigationState> changed = host.State.WaitForChangeAsync(host.State.Current.Revision).AsTask();
            await Task.WhenAll(changed, host.Client.ResetAsync(host.Root, Destination.For(new ProbeRoute(2))));
            Require(handlers.Count == 1, "A retained Single screen was acquired again.");

            stage = "cancellation";
            using (CancellationTokenSource cancellation = new())
            {
                Task waiting = handlers[0].Work!.WaitAsync(cancellation.Token);
                cancellation.Cancel();
                try
                {
                    await waiting;
                    throw new InvalidOperationException("The canceled work wait completed successfully.");
                }
                catch (OperationCanceledException)
                {
                }
                Require(!handlers[0].Work!.WaitAsync().IsCompleted, "Canceling a wait canceled the owned work.");
            }

            stage = "activation failure";
            failActivation = true;
            try
            {
                await host.Client.ResetAsync(host.Root, Destination.For(new ProbeRoute(3)));
                throw new InvalidOperationException("The controlled activation failure did not occur.");
            }
            catch (NavigationException exception)
            {
                Require(exception.DestinationCommitted, "Activation failed before destination commit.");
            }
            failActivation = false;
            NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries.Single();
            PresentationState lost = host.State.Current.GetPresentation(entry);
            Require(lost.Materialization == PresentationMaterialization.Lost, "The failed screen is not reported as lost.");

            stage = "Single recovery";
            await host.Recovery.RecoverAsync(lost.IncidentId!.Value);
            Require(host.State.Current.GetRegion(host.Root).Entries.Single() == entry, "Recovery changed the committed entry.");
            Require(((ProbeRoute)host.State.Current.GetEntry(entry).Route).Value == 3, "Recovery changed the committed route.");
            Require(handlers.Count == 2 && handlers[0].Disposed, "Recovery did not release and replace the failed instance.");

            // Lose the physical presentation while activation holds its lifecycle gate.
            // Termination must join that callback without depending on a managed worker.
            stage = "contended lifetime";
            Handler active = handlers.Last();
            active.BlockActivation = true;
            Task navigation = host.Client.ResetAsync(host.Root, Destination.For(new ProbeRoute(4)));
            await active.ActivationEntered.Task;
            active.View.Lose();
            while (host.State.Current.Presentations.Values.All(presentation => presentation.Materialization != PresentationMaterialization.Lost))
            {
                await Awaitable.NextFrameAsync();
            }
            await Awaitable.NextFrameAsync();
            active.ContinueActivation.TrySetResult(true);
            try
            {
                await navigation;
                throw new InvalidOperationException("Navigation accepted its lost presentation.");
            }
            catch (NavigationException)
            {
            }
            lost = host.State.Current.Presentations.Values.Single(presentation => presentation.Materialization == PresentationMaterialization.Lost);
            await host.Recovery.RecoverAsync(lost.IncidentId!.Value);
            Require(active.Disposed, "Recovery did not join the ending instance.");
            Require(((ProbeRoute)host.State.Current.GetEntry(lost.EntryId).Route).Value == 4, "Recovery changed the latest committed route.");

            stage = "shutdown";
            await Task.WhenAll(host.ShutdownAsync().AsTask(), host.ShutdownAsync().AsTask());
            Require(handlers.All(handler => handler.Disposed), "Shutdown retained screen resources.");
            Require(violations.Count == 0, string.Join("; ", violations));
        }

        private static void Require (bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private void Report (string message)
        {
            if (finished)
            {
                return;
            }
            finished = true;
            result = message;
            Debug.Log("NAVIGATHENA_WEBGL_RESULT " + message);
#if UNITY_WEBGL && !UNITY_EDITOR
            NavigathenaWebGLReport(message);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void NavigathenaWebGLReport (string message);
#endif

        private void OnGUI () => GUI.Label(new Rect(20, 20, 940, 620), result + "\nStage: " + stage);

        private sealed record ProbeRoute : Route
        {
            public ProbeRoute (int value) => Value = value;
            public int Value { get; }
        }

        private sealed class Handler : IScreenLifecycleHandler<ProbeRoute>, IDisposable
        {
            private readonly Func<bool> failActivation;
            private readonly List<string> violations;
            public Handler (Func<bool> failActivation, List<string> violations)
            {
                this.failActivation = failActivation;
                this.violations = violations;
            }
            public View View { get; } = new();
            public bool Disposed { get; private set; }
            public bool BlockActivation { get; set; }
            public ScreenWork? Work { get; private set; }
            public TaskCompletionSource<bool> ActivationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ContinueActivation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;
            public async ValueTask PrepareAsync (ProbeRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => await Awaitable.NextFrameAsync();
            public async ValueTask ActivateAsync (ProbeRoute route, ScreenActivityContext activity)
            {
                if (failActivation())
                {
                    throw new InvalidOperationException("Controlled activation failure.");
                }
                if (BlockActivation)
                {
                    ActivationEntered.TrySetResult(true);
                    await ContinueActivation.Task;
                }
                Work = activity.StartWork(async context =>
                {
                    while (!context.CancellationToken.IsCancellationRequested)
                    {
                        await Awaitable.NextFrameAsync();
                    }
                    await Awaitable.NextFrameAsync();
                    if (Disposed)
                    {
                        violations.Add("Screen resources were released before owned work ended.");
                    }
                });
            }
            public async ValueTask DeactivateAsync () => await Awaitable.NextFrameAsync();
            public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;
            public void Dispose () => Disposed = true;
        }

        private sealed class View : IViewAdapter
        {
            public object Identity => this;
            public object OrderingDomain => typeof(View);
            public bool IsAlive { get; private set; } = true;
            public ViewPresentation Presentation { get; private set; }
            public event Action<string>? Lost;
            public void Validate (ViewPresentation presentation) { }
            public void Apply (ViewPresentation presentation) => Presentation = presentation;
            public void Lose ()
            {
                IsAlive = false;
                Lost?.Invoke("Controlled physical loss.");
            }
        }
    }
}
