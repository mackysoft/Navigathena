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

        public NavigationEntry Entry
        {
            get;
        }
        internal int Count
        {
            get
            {
                lock (sync)
                {
                    return works.Count;
                }
            }
        }

        internal void CancelPendingSince (int index)
        {
            lock (sync)
            {
                foreach (Work work in works.Skip(index).Where(work => !work.Started))
                {
                    work.Completion.TrySetCanceled();
                }
            }
        }
        public bool IsAlive => !ending && !owner.IsEnding && owner.Entry.Id == Entry.Id;
        public bool HasPending
        {
            get
            {
                lock (sync)
                {
                    return works.Any(work => !work.Completion.Task.IsCompleted);
                }
            }
        }

        public ScreenWork Add (Func<ScreenWorkContext, ValueTask> callback)
        {
            Work work;
            lock (sync)
            {
                if (!IsAlive)
                {
                    throw new InvalidOperationException("The screen binding has ended.");
                }
                work = new Work(callback);
                works.Add(work);
            }
            runtime.ScheduleWork();
            return new ScreenWork(token => WaitAsync(work, token), () =>
            {
                try
                {
                    lock (sync)
                    {
                        if (work.Completion.Task.IsCompleted)
                        {
                            return;
                        }
                        if (!work.Started)
                        {
                            work.Completion.TrySetCanceled();
                        }
                    }
                    work.Cancellation.Cancel();
                }
                catch (ObjectDisposedException) when (work.Completion.Task.IsCompleted)
                {
                }
            });
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
                pending = works.Where(work => !work.Started && !work.Completion.Task.IsCompleted).ToArray();
                foreach (Work work in pending)
                {
                    work.Started = true;
                }
            }
            foreach (Work work in pending)
            {
                _ = RunAsync(work);
            }
        }

        private static Task WaitAsync (Work work, CancellationToken waitCancellationToken)
        {
            if (NavigationCallbackScope.IsExecuting || ScreenWorkExecution.WorkId == work.Id)
            {
                throw new InvalidOperationException("A lifecycle callback or the work itself cannot wait for owned work to finish.");
            }
            return AsyncWait.WaitAsync(work.Completion.Task, waitCancellationToken).AsTask();
        }

        private async Task RunAsync (Work work)
        {
            try
            {
                work.Cancellation.Token.ThrowIfCancellationRequested();
                using IDisposable use = owner.Use();
                bool IsValid () => IsAlive && !work.Completion.Task.IsCompleted && !work.Cancellation.IsCancellationRequested;
                IScreenCallScope calls = runtime.BindCall(Entry, owner.Id, IsValid);
                IScreenNavigation navigation = new ActivityNavigation(owner.Navigation, IsValid, runtime.State);
                ScreenWorkContext context = new(Entry.Id, navigation, work.Cancellation.Token, calls);
                await ScreenWorkExecution.RunAsync(work.Id, Entry.Id, work.Cancellation.Token, () => work.Callback(context));
                work.Cancellation.Token.ThrowIfCancellationRequested();
                work.Completion.TrySetResult(null);
            }
            catch (OperationCanceledException exception)
            {
                work.Completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                work.Completion.TrySetException(exception);
                runtime.ReportEquipmentFailure("Owned screen work failed: " + exception.Message);
            }
            finally
            {
                try
                {
                    await owner.ReleasePreviousInputAsync();
                }
                catch (Exception exception)
                {
                    runtime.ReportEquipmentFailure("Previous preparation resources could not be released: " + exception.Message);
                }
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
            List<Exception> failures = new();
            foreach (Work work in pending)
            {
                try
                {
                    work.Cancellation.Cancel();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                if (!work.Started)
                {
                    work.Completion.TrySetCanceled();
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
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                work.Cancellation.Dispose();
            }
            if (failures.Count > 0)
            {
                throw new AggregateException("Screen work could not finish safely.", failures);
            }
        }

        private sealed class Work
        {
            public Guid Id { get; } = Guid.NewGuid();
            public Work (Func<ScreenWorkContext, ValueTask> callback)
            {
                Callback = callback;
                _ = Completion.Task.ContinueWith(task =>
                {
                    _ = task.Exception;
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            public Func<ScreenWorkContext, ValueTask> Callback
            {
                get;
            }
            public CancellationTokenSource Cancellation { get; } = new();
            public TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Started
            {
                get; set;
            }
        }
    }
}
