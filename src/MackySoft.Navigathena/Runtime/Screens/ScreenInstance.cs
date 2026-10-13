using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Lifetimes;
using MackySoft.Navigathena.Runtime.Navigation;
using MackySoft.Navigathena.Runtime.Synchronization;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal sealed class ScreenInstance
    {
        private readonly AsyncGate lifecycle = new();
        private readonly INavigationStateSource state;
        private IScreenNavigation navigation;
        private readonly Action<ScreenInstance, string> reportLoss;
        private readonly Func<ScreenInstance, ValueTask> endUser;
        private readonly Action<ScreenInstance, object> claimHandler;
        private readonly TaskCompletionSource<object?> prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object terminationSync = new();
        private readonly List<ResourceScope> inputResources = new();
        private IScreenLifecycleInvocation? handler;
        private bool handlerActive;
        private bool handlerTerminated;
        private CancellationTokenSource? activity;
        private ScreenForegroundPeriod? foreground;
        private Task? stoppingActivity;
        private Task? termination;
        private int users;
        private TaskCompletionSource<object?> unused = CompletedSignal();
        private bool initialized;
        private volatile bool activityReady;
        private volatile bool activationComplete;
        private int ending;
        private Exception? activityCancellationFailure;
        private readonly IScreenActivityRuntime runtime;
        private ScreenWorkCollection work;
        internal ScreenPreparationReason? PreparationReason { get; set; }
        internal bool NavigationSettled { get; set; }
        internal bool HasWork => work.HasPending;
        internal bool HasPendingUse => HasWork || runtime.OwnsCall(Entry.Id, Id);
        internal void StartReadyWork () => work.StartReady();
        internal void PostNavigation (Action request)
        {
            CancellationToken token = activity?.Token ?? throw new InvalidOperationException("Posting navigation requires a current activity.");
            work.Add(_ =>
            {
                if (!token.IsCancellationRequested)
                {
                    request();
                }
                return default;
            }, token);
        }

        public ScreenInstance (NavigationEntry entry, PresentationContext context, RegionRouteDefinition definition, ScreenDefinition construction, ViewRegistry views, INavigationStateSource state, Action<ScreenInstance, string> reportLoss, Func<ScreenInstance, ValueTask> endUser, Action<ScreenInstance, object> claimHandler, IScreenActivityRuntime runtime)
        {
            Entry = entry;
            Definition = construction;
            Id = context.PresentationId;
            this.state = state;
            navigation = context.Navigation;
            this.reportLoss = reportLoss;
            this.endUser = endUser;
            this.claimHandler = claimHandler;
            this.runtime = runtime;
            work = new ScreenWorkCollection(this, runtime);
            Lifetime = new ResourceScope(() => this.endUser(this), reason => this.reportLoss(this, reason));
            Creation = new ManagedScreenCreationContext(entry, Lifetime, views, definition);
        }

        public NavigationEntry Entry { get; private set; }
        public ScreenDefinition Definition { get; }
        public PresentationId Id { get; }
        public ScreenInstance? Parent { get; set; }
        public int OwnershipDepth => Parent is null ? 0 : Parent.OwnershipDepth + 1;
        public ManagedScreenCreationContext Creation { get; }
        internal object? LifecycleHandler => handler?.Handler;
        public ResourceScope Lifetime { get; }
        public bool IsActive => activityReady && activity is not null && !activity.IsCancellationRequested;
        /// <summary> Is true after the current activation callback succeeds while its foreground qualification remains valid. </summary>
        internal bool IsActivationComplete => IsActive && activationComplete;
        public bool IsEnding => Volatile.Read(ref ending) != 0 || Lifetime.EndingToken.IsCancellationRequested;
        public bool IsTerminated { get; private set; }
        public ViewPresentation Presentation { get; private set; }

        public IDisposable Use ()
        {
            lock (terminationSync)
            {
                if (IsEnding)
                {
                    throw new InvalidOperationException("The screen is ending.");
                }

                if (users++ == 0)
                {
                    unused = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                return new Usage(this);
            }
        }

        public void MarkEnding ()
        {
            Interlocked.Exchange(ref ending, 1);
            InvalidateForeground();
        }

        public void InvalidateActivity ()
        {
            InvalidateForeground();
            try
            {
                activity?.Cancel();
            }
            catch (Exception exception)
            {
                activityCancellationFailure = exception;
            }
        }

        public bool DependsOn (ScreenInstance ancestor) => Parent is not null && (ReferenceEquals(Parent, ancestor) || Parent.DependsOn(ancestor));

        public void NotifyChange (PresentationChangeContext context)
        {
            if (IsEnding)
            {
                return;
            }
            if (LifecycleHandler is INavigationChangeHandler changeHandler)
            {
                changeHandler.OnNavigationChanged(context);
            }
        }

        /// <summary> Revokes operation qualification without running native or application callbacks. </summary>
        internal void InvalidateForeground ()
        {
            // Eligibility closes before native or cancellation callbacks can observe this screen.
            activityReady = false;
            activationComplete = false;
            foreground?.Invalidate();
        }

        private void EndForeground ()
        {
            ScreenForegroundPeriod? current = foreground;
            if (current is null)
            {
                return;
            }

            // A revoked period cannot be reused, even when cancellation or notification fails.
            current.Invalidate();
            foreground = null;
            List<Exception> failures = new();
            try
            {
                current.NotifyRevocation();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            current.Dispose();
            if (failures.Count > 0)
            {
                throw new AggregateException("The screen's foreground period did not end safely.", failures);
            }
        }
        public IScreenNavigation ConnectNavigation (Func<bool> connected) => new ActivityNavigation(navigation, () => connected() && IsActive, state);

        public async ValueTask PrepareAsync (INavigationOperationProgressReporter progress, CancellationToken cancellationToken, ScreenPreparationReason reason = ScreenPreparationReason.NewEntry)
        {
            await lifecycle.WaitAsync();
            try
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime.EndingToken);
                Lifetime.Progress = progress.CreateWork(NavigationPhase.Prepare, Entry.Id, "Construction");
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await Definition.CreateAsync(Creation, cancellation.Token);
                    IScreenLifecycleInvocation created = Creation.Handler!;
                    claimHandler(this, created.Handler);
                    handler = created;
                }
                finally
                {
                    try
                    {
                        await Lifetime.CloseAsync();
                    }
                    finally
                    {
                        Lifetime.Progress.Close();
                    }
                }
                cancellation.Token.ThrowIfCancellationRequested();
                NavigationProgressReporter initializationProgress = progress.CreateWork(NavigationPhase.Prepare, Entry.Id, "Initialization");
                try
                {
                    ManagedLifetimeContext initializationLifetime = Lifetime.OpenRegistration(initializationProgress);
                    ScreenInitializationContext initialization = new(initializationLifetime, initializationProgress);
                    await NavigationCallbackScope.RunAsync(() => handler.InitializeAsync(initialization, cancellation.Token));
                }
                finally
                {
                    try
                    {
                        await Lifetime.CloseAsync();
                    }
                    finally
                    {
                        initializationProgress.Close();
                    }
                }
                cancellation.Token.ThrowIfCancellationRequested();
                initialized = true;
                await PrepareInputAsync(progress, cancellation.Token, reason);
            }
            finally
            {
                lifecycle.Release();
                prepared.TrySetResult(null);
            }
        }

        public async ValueTask RebindAsync (NavigationEntry entry, PresentationContext context, INavigationOperationProgressReporter progress, CancellationToken cancellationToken, ScreenPreparationReason reason)
            => await RebindAsync(entry, context.Navigation, progress, cancellationToken, reason);

        internal IScreenNavigation Navigation => navigation;

        internal async ValueTask RebindAsync (NavigationEntry entry, IScreenNavigation nextNavigation, INavigationOperationProgressReporter progress, CancellationToken cancellationToken, ScreenPreparationReason reason = ScreenPreparationReason.Recovery)
        {
            await lifecycle.WaitAsync();
            try
            {
                if (IsActive || IsEnding || !initialized)
                {
                    throw new InvalidOperationException("Only a stopped, initialized screen can display another history entry.");
                }
                if (entry.Id != Entry.Id)
                {
                    await work.StopAsync();
                    work = new ScreenWorkCollection(this, runtime, entry);
                }
                Entry = entry;
                navigation = nextNavigation;
                await PrepareInputAsync(progress, cancellationToken, reason);
            }
            finally
            {
                lifecycle.Release();
            }
        }

        private async ValueTask PrepareInputAsync (INavigationOperationProgressReporter operationProgress, CancellationToken cancellationToken, ScreenPreparationReason reason)
        {
            NavigationProgressReporter progress = operationProgress.CreateWork(reason == ScreenPreparationReason.Recovery ? NavigationPhase.Restore : NavigationPhase.Prepare, Entry.Id, "Preparation");
            ResourceScope input = new(() => endUser(this), reason => reportLoss(this, reason))
            {
                Progress = progress
            };
            inputResources.Add(input);
            ScreenPreparationContext preparation = new(Entry, input.Context, reason, progress);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime.EndingToken, input.EndingToken);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await NavigationCallbackScope.RunAsync(() => handler!.PrepareAsync(Entry.Route, preparation, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                PreparationReason = reason;
            }
            finally
            {
                try
                {
                    await input.CloseAsync();
                }
                finally
                {
                    progress.Close();
                }
            }
        }

        public async ValueTask ReleasePreviousInputAsync (INavigationOperationProgressReporter? operationProgress, bool retainForCalls = true)
        {
            await lifecycle.WaitAsync();
            try
            {
                if (HasWork || (retainForCalls && runtime.OwnsCall(Entry.Id, Id)) || IsEnding)
                {
                    return;
                }
                while (inputResources.Count > 1)
                {
                    NavigationProgressReporter progress = operationProgress?.CreateWork(NavigationPhase.Cleanup, Entry.Id, "PreparationRelease") ?? NavigationProgressReporter.None;
                    try
                    {
                        await inputResources[0].ReleaseAsync(progress);
                    }
                    finally
                    {
                        progress.Close();
                    }
                    inputResources.RemoveAt(0);
                }
            }
            finally
            {
                lifecycle.Release();
            }
        }

        public async ValueTask ActivateAsync (ScreenActivationReason reason = ScreenActivationReason.Recovery)
        {
            await lifecycle.WaitAsync();
            try
            {
                if (IsEnding || !initialized)
                {
                    throw new InvalidOperationException("The screen is not available for activity.");
                }

                if (IsActive)
                {
                    return;
                }

                if (!Presentation.OutputEnabled || !Presentation.InputEnabled)
                {
                    throw new InvalidOperationException("Activation requires completed native input admission.");
                }

                if (stoppingActivity is not null)
                {
                    await stoppingActivity;
                }

                CancellationTokenSource current = new();
                activity = current;
                activityCancellationFailure = null;
                stoppingActivity = null;
                foreground = new ScreenForegroundPeriod();
                ScreenForegroundToken foregroundToken = new(Entry.Id, Entry.RegionId, foreground);
                // Activation is the application's operation entry, not another preparation phase.
                // All affected native adapters finish admission before any activation is invoked.
                activationComplete = false;
                activityReady = true;
                ActivityNavigation bound = new(navigation, () => activityReady && !current.IsCancellationRequested && !IsEnding, state,
                    () => !current.IsCancellationRequested && !IsEnding);
                try
                {
                    ScreenWorkCollection binding = work;
                    ScreenActivityContext context = new(Entry.Id, Entry.RegionId, bound, current.Token, foregroundToken,
                        runtime.BindCall(Entry, Id, () => IsActive && !current.IsCancellationRequested),
                        callback =>
                        {
                            if (!IsActive || current.IsCancellationRequested || IsEnding || !ReferenceEquals(binding, work))
                            {
                                throw new InvalidOperationException("The activity that started this work is no longer valid.");
                            }
                            return binding.Add(callback, current.Token);
                        }, !runtime.HasActivated(Entry.Id), reason, PreparationReason);
                    current.Token.ThrowIfCancellationRequested();
                    handlerActive = true;
                    await NavigationCallbackScope.RunAsync(() => handler!.ActivateAsync(Entry.Route, context));
                    current.Token.ThrowIfCancellationRequested();
                    activationComplete = true;
                }
                catch (Exception failure)
                {
                    List<Exception> failures = new() { failure };
                    // A partially entered screen must lose qualification and native input
                    // before unsubscribe cleanup or cancellation callbacks can run.
                    try
                    {
                        await ApplyAsync(new ViewPresentation(!IsEnding && Presentation.OutputEnabled, false, Presentation.Order));
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                    try
                    {
                        await StopActivityAsync();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                    if (failures.Count > 1)
                    {
                        throw new AggregateException("Screen activation and cleanup failed.", failures);
                    }
                    throw;
                }
            }
            finally
            {
                lifecycle.Release();
            }
        }

        public async ValueTask DeactivateAsync ()
        {
            await lifecycle.WaitAsync();
            try
            {
                await StopActivityAsync();
            }
            finally
            {
                lifecycle.Release();
            }
        }

        public async ValueTask CloseActivityAsync ()
        {
            Exception? failure = null;
            try
            {
                await ApplyAsync(new ViewPresentation(!IsEnding && Presentation.OutputEnabled, false, Presentation.Order));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            InvalidateActivity();
            try
            {
                await DeactivateAsync();
            }
            catch (Exception exception)
            {
                if (failure is not null)
                {
                    throw new AggregateException(failure, exception);
                }
                throw;
            }
            if (failure is not null)
            {
                throw failure;
            }
        }

        private ValueTask StopActivityAsync ()
        {
            if (stoppingActivity is not null)
            {
                return new ValueTask(stoppingActivity);
            }
            if (activity is null)
            {
                return default;
            }

            return new ValueTask(stoppingActivity = StopActivityCoreAsync(activity));
        }

        private async Task StopActivityCoreAsync (CancellationTokenSource current)
        {
            activityReady = false;
            work.CancelPendingFrom(current.Token);
            // Invalidate the navigation generation before invoking cancellation or user callbacks.
            Exception? cancellationFailure = activityCancellationFailure;
            try
            {
                current.Cancel();
            }
            catch (Exception exception)
            {
                cancellationFailure = exception;
            }

            List<Exception> failures = new();
            if (cancellationFailure is not null)
            {
                failures.Add(cancellationFailure);
            }
            if (handlerActive)
            {
                try
                {
                    await NavigationCallbackScope.RunAsync(handler!.DeactivateAsync);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (ReferenceEquals(activity, current))
            {
                activity = null;
            }
            current.Dispose();
            if (failures.Count > 0)
            {
                throw new AggregateException("Screen activity did not stop safely.", failures);
            }
            handlerActive = false;
        }

        public void Apply (ViewPresentation presentation)
        {
            if (IsEnding && presentation.OutputEnabled)
            {
                throw new InvalidOperationException("An ending screen cannot be published.");
            }

            bool closing = !presentation.InputEnabled || !presentation.OutputEnabled;
            if (closing)
            {
                InvalidateForeground();
            }

            try
            {
                foreach (ViewRegistration view in Creation.Registrations)
                {
                    view.Apply(presentation);
                }

                Presentation = presentation;
            }
            catch (Exception failure)
            {
                if (closing)
                {
                    try
                    {
                        EndForeground();
                    }
                    catch (Exception endingFailure)
                    {
                        throw new AggregateException("Presentation and foreground closure failed.", failure, endingFailure);
                    }
                }
                throw;
            }
            if (closing)
            {
                EndForeground();
            }
        }

        public ValueTask ApplyAsync (ViewPresentation presentation)
            => ApplyBatchAsync(new[] { (Screen: this, Presentation: presentation) });

        /// <summary>Publishes a complete permission phase before admitting new input.</summary>
        internal static async ValueTask ApplyBatchAsync (IReadOnlyList<(ScreenInstance Screen, ViewPresentation Presentation)> updates)
        {
            List<ViewPresentationChange> changes = new();
            List<Exception> failures = new();
            foreach (var update in updates)
            {
                if (!update.Presentation.InputEnabled || !update.Presentation.OutputEnabled)
                {
                    update.Screen.InvalidateForeground();
                }
            }
            foreach (var update in updates)
            {
                if (update.Screen.IsEnding && update.Presentation.OutputEnabled)
                {
                    throw new InvalidOperationException("An ending screen cannot be published.");
                }
                foreach (ViewRegistration view in update.Screen.Creation.Registrations)
                {
                    if (!update.Presentation.OutputEnabled && !update.Presentation.InputEnabled && !view.Adapter.IsAlive)
                    {
                        continue;
                    }
                    try
                    {
                        changes.Add(view.CreateChange(update.Presentation));
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            if (failures.Count != 0)
            {
                foreach (var update in updates)
                {
                    update.Screen.InvalidateForeground();
                }
                // A view rejected during validation is still part of the failed phase.
                // Close every live participant using its last applied native order.
                changes = updates.SelectMany(update => update.Screen.Creation.Registrations)
                    .Select(view => view.Adapter).Distinct().Where(view => view.IsAlive)
                    .Select(view => new ViewPresentationChange(view,
                        new ViewPresentation(view.Presentation.OutputEnabled, false, view.Presentation.Order, view.Presentation.InputMode))).ToList();
            }
            try
            {
                await ViewPresentationApplication.ApplyAsync(new ViewPresentationChangeSet(changes));
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                foreach (var update in updates)
                {
                    update.Screen.InvalidateForeground();
                }
                try
                {
                    ViewPresentationChange[] closing = updates.SelectMany(update => update.Screen.Creation.Registrations)
                        .Select(view => view.Adapter).Distinct().Where(view => view.IsAlive)
                        .Select(view => new ViewPresentationChange(view,
                            new ViewPresentation(view.Presentation.OutputEnabled, false,
                                view.Presentation.Order, view.Presentation.InputMode))).ToArray();
                    await ViewPresentationApplication.ApplyAsync(new ViewPresentationChangeSet(closing));
                }
                catch (Exception closureFailure)
                {
                    failures.Add(closureFailure);
                }
            }
            // Native closure precedes lifecycle notification. Failed phases also revoke every old period.
            foreach (var update in updates)
            {
                if (failures.Count != 0 || !update.Presentation.InputEnabled || !update.Presentation.OutputEnabled)
                {
                    try
                    {
                        update.Screen.EndForeground();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Screen presentation could not be applied; affected input remains closed.", failures);
            }
            foreach (var update in updates)
            {
                update.Screen.Presentation = update.Presentation;
            }
        }

        public PresentationStateCapture? CaptureState ()
        {
            IScreenStateCapture? capture = LifecycleHandler as IScreenStateCapture;
            return capture is null ? null : new PresentationStateCapture(Entry.Id, Id, capture.CaptureState());
        }

        public ValueTask TerminateAsync (INavigationOperationProgressReporter? progress)
        {
            MarkEnding();
            TaskCompletionSource<object?> completion;
            lock (terminationSync)
            {
                if (termination is not null)
                {
                    return new ValueTask(termination);
                }

                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                termination = completion.Task;
            }

            _ = FinishTerminationAsync(completion, progress);
            return new ValueTask(completion.Task);
        }

        private async Task FinishTerminationAsync (TaskCompletionSource<object?> completion, INavigationOperationProgressReporter? progress)
        {
            try
            {
                await TerminateCoreAsync(progress);
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }

        private async Task TerminateCoreAsync (INavigationOperationProgressReporter? operationProgress)
        {
            await prepared.Task;
            List<Exception> inputFailures = new();
            try
            {
                await ApplyAsync(new ViewPresentation(false, false, Presentation.Order));
            }
            catch (Exception exception)
            {
                inputFailures.Add(exception);
            }
            try
            {
                InvalidateActivity();
            }
            catch (Exception exception)
            {
                inputFailures.Add(exception);
            }

            // Activity and work can both use instance resources while stopping. Start both
            // stops before joining them so deactivation can release work-specific waits.
            Task stoppingWork = work.StopAsync().AsTask();
            await Task.WhenAll(stoppingWork, DeactivateAsync().AsTask());
            await unused.Task;
            await lifecycle.WaitAsync();
            try
            {
                if (handler is not null && !handlerTerminated)
                {
                    NavigationProgressReporter progress = operationProgress?.CreateWork(NavigationPhase.Cleanup, Entry.Id, "Termination") ?? NavigationProgressReporter.None;
                    try
                    {
                        await NavigationCallbackScope.RunAsync(() => handler.TerminateAsync(progress));
                    }
                    finally
                    {
                        progress.Close();
                    }
                    handlerTerminated = true;
                }

                Creation.TransitionView?.Release();
                Creation.ReleasePresentation();
                NavigationProgressReporter releaseProgress = operationProgress?.CreateWork(NavigationPhase.Cleanup, Entry.Id, "ResourceRelease") ?? NavigationProgressReporter.None;
                try
                {
                    for (int i = inputResources.Count - 1; i >= 0; i--)
                    {
                        await inputResources[i].ReleaseAsync(releaseProgress);
                    }
                    inputResources.Clear();
                    await Lifetime.ReleaseAsync(releaseProgress);
                }
                finally
                {
                    releaseProgress.Close();
                }
                IsTerminated = true;
                if (inputFailures.Count > 0)
                {
                    throw new AggregateException("Screen ended, but native input closure failed.", inputFailures);
                }
            }
            finally
            {
                lifecycle.Release();
            }
        }

        private static TaskCompletionSource<object?> CompletedSignal ()
        {
            TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            completion.SetResult(null);
            return completion;
        }

        private sealed class Usage : IDisposable
        {
            private ScreenInstance? owner;
            public Usage (ScreenInstance owner) => this.owner = owner;
            public void Dispose ()
            {
                ScreenInstance? current = Interlocked.Exchange(ref owner, null);
                if (current is null)
                {
                    return;
                }

                lock (current.terminationSync)
                {
                    if (--current.users == 0)
                    {
                        current.unused.TrySetResult(null);
                    }
                }
            }
        }
    }
}
