using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Runtime.Resources;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Blockers
{
    internal sealed class BlockerInstance
    {
        private readonly BlockerFactory factory;
        private IBlockerPresenter? presenter;
        private readonly Action<BlockerInstance, string> reportFailure;
        private readonly List<Task> animations = new();
        private readonly TaskCompletionSource<object?> prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? animationCancellation;
        private Task? termination;
        private long generation;
        private bool connected;
        private bool ending;
        private bool shown;
        private readonly IDisposable? parentUsage;
        public BlockerInstance (BlockerFactory factory, ViewRegistry views, Func<BlockerInstance, ValueTask> endUser, Action<BlockerInstance, string> reportFailure, ScreenInstance? parent)
        {
            Parent = parent;
            parentUsage = parent?.Use();
            this.factory = factory;
            this.reportFailure = reportFailure;
            Resources = new ResourceScope(() => endUser(this), reason =>
{
    ending = true;
    reportFailure(this, reason);
});
            Preparation = new ManagedBlockerPreparationContext(Resources, views);
        }
        public ResourceScope Resources
        {
            get;
        }
        public ManagedBlockerPreparationContext Preparation
        {
            get;
        }
        public bool IsEnding => ending || Resources.EndingToken.IsCancellationRequested;
        public bool IsTerminated
        {
            get; private set;
        }
        public bool IsShown => shown;
        public bool HasOutput => Preparation.Registrations.Any(view => view.Adapter.IsAlive && view.Adapter.Presentation.OutputEnabled);
        public ScreenInstance? OrderAnchor
        {
            get; private set;
        }
        public ScreenInstance? Screen
        {
            get; private set;
        }
        public ScreenInstance? Parent
        {
            get;
        }

        public async ValueTask PrepareAsync (CancellationToken cancellationToken)
        {
            try
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Resources.EndingToken);
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    presenter = await factory(Preparation, cancellation.Token) ?? throw new NavigationConfigurationException("The blocker factory returned null.");
                    cancellation.Token.ThrowIfCancellationRequested();
                    await presenter.PrepareAsync(Preparation, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                finally
                {
                    await Resources.CloseAsync();
                }
                cancellation.Token.ThrowIfCancellationRequested();
            }
            finally
            {
                prepared.TrySetResult(null);
            }
        }

        public void CloseInput ()
        {
            connected = false;
            Apply(shown, false);
        }

        public void Connect (ScreenInstance screen)
        {
            if (IsEnding)
            {
                throw new InvalidOperationException("An ending blocker cannot be connected.");
            }

            generation++;
            long connection = generation;
            connected = false;
            Screen = screen;
            OrderAnchor = screen;
            presenter?.SetScreenContext(new BlockerScreenContext(screen.Entry, screen.ConnectNavigation(() => generation == connection && connected && !IsEnding)));
        }

        public void OpenInput ()
        {
            connected = shown && Screen?.IsActive == true && !IsEnding;
            Apply(shown, connected);
        }

        public void Show (bool animate)
        {
            bool alreadyShown = shown;
            shown = true;
            Apply(true, false);
            if (alreadyShown)
            {
                InvalidateAnimation(true);
                return;
            }

            InvalidateAnimation(animate ? false : true);
            if (animate)
            {
                BeginAnimation(true);
            }
        }

        public void Hide (bool animate)
        {
            generation++;
            connected = false;
            Screen = null;
            presenter?.SetScreenContext(null);
            InvalidateAnimation(animate);
            shown = false;
            if (animate && presenter is IBlockerAnimator)
            {
                Apply(true, false);
                BeginAnimation(false);
            }
            else
            {
                Apply(false, false);
                OrderAnchor = null;
            }
        }

        private void InvalidateAnimation (bool appearance)
        {
            Exception? failure = null;
            try
            {
                animationCancellation?.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            (presenter as IBlockerAnimator)?.SetAppearanceImmediately(appearance);
            if (failure is not null)
            {
                throw failure;
            }
        }

        private void BeginAnimation (bool enter)
        {
            if (presenter is not IBlockerAnimator animator)
            {
                return;
            }

            animations.RemoveAll(task => task.IsCompletedSuccessfully);
            CancellationTokenSource cancellation = new();
            animationCancellation = cancellation;
            Task animation = ObserveAnimationAsync(animator, enter, cancellation);
            animations.Add(animation);
            _ = animation.ContinueWith(task =>
{
    _ = task.Exception;
}, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private async Task ObserveAnimationAsync (IBlockerAnimator animator, bool enter, CancellationTokenSource cancellation)
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (enter)
                {
                    await animator.PlayEnterAsync(cancellation.Token);
                }
                else
                {
                    await animator.PlayExitAsync(cancellation.Token);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (!cancellation.IsCancellationRequested && ReferenceEquals(animationCancellation, cancellation))
                {
                    animator.SetAppearanceImmediately(enter);
                    if (!enter)
                    {
                        Apply(false, false);
                        OrderAnchor = null;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                ending = true;
                connected = false;
                reportFailure(this, exception.Message);
                throw;
            }
        }

        private void Apply (bool output, bool input)
        {
            foreach (ViewRegistration view in Preparation.Registrations)
            {
                if (view.Adapter.IsAlive)
                {
                    view.Apply(new ViewPresentation(output, input, view.Adapter.Presentation.Order));
                }
                else if (output)
                {
                    throw new InvalidOperationException("The blocker view is unavailable.");
                }
            }
        }

        public async ValueTask DisconnectAsync ()
        {
            List<Exception> failures = new();
            try
            {
                Hide(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            try
            {
                await Task.WhenAll(animations);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("The blocker could not finish using its previous screen.", failures);
            }
        }

        public ValueTask TerminateAsync ()
        {
            if (termination is not null)
            {
                return new ValueTask(termination);
            }

            ending = true;
            TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            termination = completion.Task;
            _ = TerminateCoreAsync(completion);
            return new ValueTask(completion.Task);
        }

        private async Task TerminateCoreAsync (TaskCompletionSource<object?> completion)
        {
            try
            {
                await prepared.Task;
                Exception? disconnectFailure = null;
                try
                {
                    await DisconnectAsync();
                }
                catch (Exception exception)
                {
                    disconnectFailure = exception;
                }
                if (presenter is not null)
                {
                    await presenter.TerminateAsync();
                }
                foreach (ViewRegistration view in Preparation.Registrations)
                {
                    view.Release();
                }

                await Resources.DisposeAsync();
                parentUsage?.Dispose();
                IsTerminated = true;
                if (disconnectFailure is not null)
                {
                    throw disconnectFailure;
                }
                completion.SetResult(null);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }
    }
}
