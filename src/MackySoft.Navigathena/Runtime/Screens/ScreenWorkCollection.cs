using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Runtime.Navigation;

namespace MackySoft.Navigathena.Runtime.Screens
{
    internal sealed class ScreenWorkCollection
    {
        private readonly object sync = new();
        private readonly ScreenInstance owner;
        private readonly IScreenActivityRuntime runtime;
        private readonly List<Work> works = new();
        private bool ending;

        public ScreenWorkCollection (ScreenInstance owner, IScreenActivityRuntime runtime, NavigationEntry? entry = null)
        {
            this.owner = owner;
            this.runtime = runtime;
            Entry = entry ?? owner.Entry;
        }

        public NavigationEntry Entry { get; }
        public bool IsAlive => !ending && !owner.IsEnding && owner.Entry.Id == Entry.Id;
        public bool HasPending
        {
            get
            {
                lock (sync)
                {
                    return works.Count != 0;
                }
            }
        }

        internal void CancelPendingFrom (CancellationToken activity)
        {
            lock (sync)
            {
                // Completed work can disappear during activation, so collection positions are not ownership.
                foreach (Work work in works.Where(work => !work.Started && work.Activity == activity).ToArray())
                {
                    Complete(work, new OperationCanceledException(activity));
                }
            }
        }

        public ScreenWork Add (Func<ScreenWorkContext, ValueTask> callback, CancellationToken activity)
        {
            Work work = new(callback, activity);
            ScreenWork result = new(work.Id, work.Completion.Task, () => Cancel(work));
            work.Result = result;
            lock (sync)
            {
                if (!IsAlive)
                {
                    work.Cancellation.Dispose();
                    throw new InvalidOperationException("The screen binding has ended.");
                }
                works.Add(work);
            }
            runtime.ScheduleWork();
            return result;
        }

        private void Cancel (Work work)
        {
            lock (sync)
            {
                if (work.Callback is null)
                {
                    return;
                }
                if (!work.Started)
                {
                    Complete(work, new OperationCanceledException());
                    return;
                }
                if (work.CancellationRequests++ == 0)
                {
                    work.CancellationFinished = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            // Cancellation invokes user callbacks. Keep them outside the collection lock and
            // let completion wait for them before disposing the source or releasing the binding.
            try
            {
                work.Cancellation.Cancel();
            }
            finally
            {
                lock (sync)
                {
                    if (--work.CancellationRequests == 0)
                    {
                        work.CancellationFinished!.TrySetResult(null);
                    }
                }
            }
        }

        public void StartReady ()
        {
            Work[] pending;
            lock (sync)
            {
                if (!IsAlive || !owner.IsActive || !owner.NavigationSettled)
                {
                    return;
                }
                pending = works.Where(work => !work.Started).ToArray();
                foreach (Work work in pending)
                {
                    work.Started = true;
                    work.Activity = default;
                }
            }
            foreach (Work work in pending)
            {
                _ = RunAsync(work);
            }
        }

        private async Task RunAsync (Work work)
        {
            Exception? failure = null;
            try
            {
                work.Cancellation.Token.ThrowIfCancellationRequested();
                using IDisposable use = owner.Use();
                bool IsValid () => IsAlive && !work.Completion.Task.IsCompleted && !work.Cancellation.IsCancellationRequested;
                IScreenCallScope calls = runtime.BindCall(Entry, owner.Id, IsValid);
                IScreenNavigation navigation = new ActivityNavigation(owner.Navigation, IsValid, runtime.State);
                ScreenWorkContext context = new(Entry.Id, navigation, work.Cancellation.Token, calls);
                await ScreenWorkExecution.RunAsync(work.Id, Entry.Id, work.Cancellation.Token, () => work.Callback!(context));
                work.Cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }
            catch (Exception exception)
            {
                failure = exception;
                runtime.ReportEquipmentFailure("Owned screen work failed: " + exception.Message);
            }

            Task cancellationFinished;
            lock (sync)
            {
                work.Callback = null;
                cancellationFinished = work.CancellationFinished?.Task ?? Task.CompletedTask;
            }
            await cancellationFinished;
            lock (sync)
            {
                // The callback, its finally blocks, cancellation callbacks and usage lease have ended.
                Complete(work, failure);
            }

            // Rebinding can hold the lifecycle lock while joining work. Resource cleanup must
            // therefore follow completion, independently of the work's use of those resources.
            try
            {
                await owner.ReleasePreviousInputAsync(null);
            }
            catch (Exception exception)
            {
                runtime.ReportEquipmentFailure("Previous preparation resources could not be released: " + exception.Message);
            }
        }

        private void Complete (Work work, Exception? failure)
        {
            CancellationToken cancellationToken = work.Cancellation.Token;
            bool canceled = cancellationToken.IsCancellationRequested;
            work.Callback = null;
            work.Result!.DetachCancellation();
            work.Cancellation.Dispose();
            works.Remove(work);
            if (failure is OperationCanceledException cancellation)
            {
                work.Completion.TrySetCanceled(cancellation.CancellationToken);
            }
            else if (failure is not null)
            {
                work.Completion.TrySetException(failure);
            }
            else if (canceled)
            {
                work.Completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                work.Completion.TrySetResult(null);
            }
        }

        public async ValueTask StopAsync ()
        {
            Work[] pending;
            lock (sync)
            {
                ending = true;
                pending = works.ToArray();
            }
            runtime.EndBindingCalls(Entry.Id, owner.Id);
            foreach (Work work in pending)
            {
                try
                {
                    Cancel(work);
                }
                catch (Exception exception)
                {
                    runtime.ReportEquipmentFailure("Canceling owned screen work failed: " + exception.Message);
                }
            }
            foreach (Work work in pending)
            {
                try
                {
                    await work.Completion.Task;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    // The result handle and diagnostics preserve failure. A finished callback
                    // no longer uses the screen and must not prevent termination and release.
                }
            }
        }

        private sealed class Work
        {
            public Guid Id { get; } = Guid.NewGuid();
            public Work (Func<ScreenWorkContext, ValueTask> callback, CancellationToken activity)
            {
                Callback = callback;
                Activity = activity;
                _ = Completion.Task.ContinueWith(task =>
                {
                    _ = task.Exception;
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            public Func<ScreenWorkContext, ValueTask>? Callback { get; set; }
            public CancellationToken Activity { get; set; }
            public CancellationTokenSource Cancellation { get; } = new();
            public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ScreenWork? Result { get; set; }
            public bool Started { get; set; }
            public int CancellationRequests { get; set; }
            public TaskCompletionSource<object?>? CancellationFinished { get; set; }
        }
    }
}
