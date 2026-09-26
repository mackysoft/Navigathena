using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Lifetimes
{
    internal sealed class ResourceScope : IAsyncDisposable, IResourceUser
    {
        private readonly object sync = new();
        private readonly List<IAsyncDisposable> acquisitions = new();
        private readonly HashSet<ResourceLifetime> borrowed = new();
        private readonly Func<ValueTask> endUser;
        private readonly Action<string> reportLoss;
        private readonly TaskCompletionSource<object?> drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource ending = new();
        private bool open = true;
        private int pending;
        private Task? disposal;
        private Task? ownedDisposal;
        private Task? requestedEnd;

        public ResourceScope (Func<ValueTask> endUser, Action<string> reportLoss)
        {
            this.endUser = endUser;
            this.reportLoss = reportLoss;
            Context = new ManagedLifetimeContext(this);
        }

        public LifetimeContext Context
        {
            get;
        }
        public CancellationToken EndingToken => ending.Token;
        public void ReportLoss (string reason) => reportLoss(reason);

        public T CreateOwned<T> (Func<T> create) where T : class
        {
            if (create is null)
            {
                throw new ArgumentNullException(nameof(create));
            }
            OwnedObject<T> owner = new();
            lock (sync)
            {
                EnsureOpen();
                acquisitions.Add(owner);
                pending++;
            }
            try
            {
                T value = create() ?? throw new InvalidOperationException("An owned construction returned null.");
                lock (sync)
                {
                    if (acquisitions.OfType<IOwnedObject>().Any(item => ReferenceEquals(item.Instance, value)))
                    {
                        throw new InvalidOperationException("An object cannot be owned twice by the same resource scope.");
                    }
                    owner.Value = value;
                }
                return owner.Value;
            }
            finally
            {
                lock (sync)
                {
                    pending--;
                    if (!open && pending == 0)
                    {
                        drained.TrySetResult(null);
                    }
                }
            }
        }

        private interface IOwnedObject : IAsyncDisposable
        {
            object? Instance
            {
                get;
            }
        }

        private sealed class OwnedObject<T> : IOwnedObject where T : class
        {
            public T? Value
            {
                get; set;
            }
            public object? Instance => Value;
            public async ValueTask DisposeAsync ()
            {
                if (Value is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync();
                }
                else if (Value is IDisposable synchronous)
                {
                    synchronous.Dispose();
                }
                Value = null;
            }
        }

        public void EnsureOpen ()
        {
            lock (sync)
            {
                if (!open || ending.IsCancellationRequested)
                {
                    throw new InvalidOperationException("Resource registration is only available during preparation.");
                }
            }
        }

        public async ValueTask<T> AcquireAsync<T> (IResourceAcquisition<T> acquisition, CancellationToken cancellationToken)
        {
            if (acquisition is null)
            {
                throw new ArgumentNullException(nameof(acquisition));
            }

            lock (sync)
            {
                EnsureOpen();
                if (acquisitions.Any(item => ReferenceEquals(item, acquisition)))
                {
                    throw new InvalidOperationException("Each acquisition object can only be registered once.");
                }

                acquisitions.Add(acquisition);
                pending++;
            }

            try
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ending.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                T value = await acquisition.AcquireAsync(new ResourceAcquisitionContext(reportLoss), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                return value;
            }
            finally
            {
                lock (sync)
                {
                    pending--;
                    if (!open && pending == 0)
                    {
                        drained.TrySetResult(null);
                    }
                }
            }
        }

        public T Borrow<T> (ResourceReference<T> resource) where T : class
        {
            lock (sync)
            {
                EnsureOpen();
                resource.Lifetime.AddUser(this);
                borrowed.Add(resource.Lifetime);
                return resource.Value;
            }
        }

        public async ValueTask CloseAsync ()
        {
            bool unfinished;
            lock (sync)
            {
                unfinished = pending != 0;
                open = false;
                if (!unfinished)
                {
                    drained.TrySetResult(null);
                }
            }

            await drained.Task;
            if (unfinished)
            {
                throw new InvalidOperationException("Preparation returned before all resource acquisitions were awaited.");
            }
        }

        public ValueTask RequestEndAsync ()
        {
            TaskCompletionSource<object?>? completion = null;
            Task task;
            lock (sync)
            {
                if (requestedEnd is null)
                {
                    completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    requestedEnd = completion.Task;
                }

                task = requestedEnd;
            }

            if (completion is not null)
            {
                _ = EndUserAsync(completion);
            }

            return new ValueTask(task);
        }

        private async Task EndUserAsync (TaskCompletionSource<object?> completion)
        {
            try
            {
                Exception? cancellationFailure = null;
                try
                {
                    ending.Cancel();
                }
                catch (Exception exception)
                {
                    cancellationFailure = exception;
                }
                await endUser();
                if (cancellationFailure is not null)
                {
                    throw cancellationFailure;
                }

                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        public ValueTask DisposeAsync ()
        {
            TaskCompletionSource<object?>? completion = null;
            Task task;
            lock (sync)
            {
                if (disposal is null)
                {
                    completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    disposal = completion.Task;
                    open = false;
                    if (pending == 0)
                    {
                        drained.TrySetResult(null);
                    }
                }

                task = disposal;
            }

            if (completion is not null)
            {
                _ = DisposeResourcesAsync(completion);
            }

            return new ValueTask(task);
        }

        private async Task DisposeResourcesAsync (TaskCompletionSource<object?> completion)
        {
            try
            {
                await drained.Task;
                await ReleaseOwnedAsync();
                // Acquisitions can depend on earlier acquisitions. A failed dependent must retain them.
                for (int i = acquisitions.Count - 1; i >= 0; i--)
                {
                    await acquisitions[i].DisposeAsync();
                    acquisitions.RemoveAt(i);
                }

                foreach (ResourceLifetime lifetime in borrowed)
                {
                    lifetime.RemoveUser(this);
                }

                borrowed.Clear();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        public ValueTask ReleaseOwnedAsync () => new(ownedDisposal ??= ReleaseOwnedCoreAsync());

        private async Task ReleaseOwnedCoreAsync ()
        {
            await drained.Task;
            foreach (IOwnedObject owner in acquisitions.OfType<IOwnedObject>().Reverse().ToArray())
            {
                await owner.DisposeAsync();
                acquisitions.Remove(owner);
            }
        }
    }
}
