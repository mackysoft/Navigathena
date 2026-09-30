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
        private readonly List<IResourceOwnership> acquisitions = new();
        private readonly HashSet<ResourceLifetime> borrowed = new();
        private readonly Func<ValueTask> endUser;
        private readonly Action<string> reportLoss;
        private TaskCompletionSource<object?> drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource ending = new();
        private bool open = true;
        private int pending;
        private Task? disposal;
        private Task? requestedEnd;

        public ResourceScope (Func<ValueTask> endUser, Action<string> reportLoss)
        {
            this.endUser = endUser;
            this.reportLoss = reportLoss;
            Registration = new ManagedLifetimeContext(this, NavigationProgressReporter.None);
        }

        internal ManagedLifetimeContext Registration { get; private set; }
        public LifetimeContext Context => Registration;
        public CancellationToken EndingToken => ending.Token;
        internal NavigationProgressReporter Progress
        {
            get => Registration.Progress;
            set => Registration.Progress = value;
        }
        public void ReportLoss (string reason) => reportLoss(reason);

        internal ManagedLifetimeContext OpenRegistration (NavigationProgressReporter progress)
        {
            lock (sync)
            {
                if (open || pending != 0 || disposal is not null || ending.IsCancellationRequested)
                {
                    throw new InvalidOperationException("Resource registration cannot start while another callback or release is running.");
                }

                // A new callback gets its own capability; retained creation contexts stay closed.
                Registration = new ManagedLifetimeContext(this, progress);
                drained = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                open = true;
                return Registration;
            }
        }

        public T CreateOwned<T> (ManagedLifetimeContext registration, Func<T> create) where T : class
        {
            if (create is null)
            {
                throw new ArgumentNullException(nameof(create));
            }
            OwnedObject<T> owner = new();
            lock (sync)
            {
                EnsureOpen(registration);
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

        private interface IResourceOwnership
        {
            object? Identity { get; }
            ValueTask ReleaseAsync (NavigationProgressReporter progress);
        }

        private interface IOwnedObject : IResourceOwnership
        {
            object? Instance { get; }
        }

        private sealed class OwnedObject<T> : IOwnedObject where T : class
        {
            public T? Value { get; set; }
            public object? Instance => Value;
            public object? Identity => Value;
            public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
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

        private sealed class AcquiredResource<T> : IResourceOwnership
        {
            private readonly IResourceAcquisition<T> acquisition;

            public AcquiredResource (IResourceAcquisition<T> acquisition) => this.acquisition = acquisition;
            public object Identity => acquisition;

            public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
            {
                NavigationProgressReporter release = progress.CreateChild(acquisition.GetType().Name);
                try
                {
                    await acquisition.ReleaseAsync(release);
                }
                finally
                {
                    release.Close();
                }
            }
        }

        public void EnsureOpen () => EnsureOpen(Registration);

        internal void EnsureOpen (ManagedLifetimeContext registration)
        {
            lock (sync)
            {
                if (!open || !ReferenceEquals(registration, Registration) || ending.IsCancellationRequested)
                {
                    throw new InvalidOperationException("Resource registration is only available during its owning callback.");
                }
            }
        }

        public async ValueTask<T> AcquireAsync<T> (ManagedLifetimeContext registration, IResourceAcquisition<T> acquisition, CancellationToken cancellationToken)
        {
            if (acquisition is null)
            {
                throw new ArgumentNullException(nameof(acquisition));
            }

            lock (sync)
            {
                EnsureOpen(registration);
                if (acquisitions.Any(item => ReferenceEquals(item.Identity, acquisition)))
                {
                    throw new InvalidOperationException("Each acquisition object can only be registered once.");
                }

                acquisitions.Add(new AcquiredResource<T>(acquisition));
                pending++;
            }

            try
            {
                using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ending.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                NavigationProgressReporter progress = registration.Progress.CreateChild(acquisition.GetType().Name);
                try
                {
                    T value = await acquisition.AcquireAsync(new ResourceAcquisitionContext(reportLoss, progress), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    return value;
                }
                finally
                {
                    progress.Close();
                }
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

        public T Borrow<T> (ManagedLifetimeContext registration, ResourceReference<T> resource) where T : class
        {
            lock (sync)
            {
                EnsureOpen(registration);
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

        public ValueTask DisposeAsync () => ReleaseAsync(NavigationProgressReporter.None);

        public ValueTask ReleaseAsync (NavigationProgressReporter progress)
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
                _ = DisposeResourcesAsync(completion, progress);
            }

            return new ValueTask(task);
        }

        private async Task DisposeResourcesAsync (TaskCompletionSource<object?> completion, NavigationProgressReporter progress)
        {
            try
            {
                await drained.Task;
                // Both owned objects and acquisitions may depend on earlier registrations.
                // A failed release must retain every earlier dependency.
                for (int i = acquisitions.Count - 1; i >= 0; i--)
                {
                    await acquisitions[i].ReleaseAsync(progress);
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

    }
}
