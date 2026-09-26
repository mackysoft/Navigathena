using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>A host-owned operation with independent result waiting and explicit cancellation.</summary>
    public sealed class NavigationOperation
    {
        private readonly object sync = new();
        private readonly CancellationTokenSource cancellation = new();
        private readonly TaskCompletionSource<NavigationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool cancellationRequested;
        private bool irrevocable;
        private bool completed;
        internal IReadOnlyCollection<NavigationEntryId> RemovedEntries { get; set; } = Array.Empty<NavigationEntryId>();

        internal NavigationOperation () => Id = new NavigationOperationId(Guid.NewGuid());
        public NavigationOperationId Id
        {
            get;
        }
        internal CancellationToken CancellationToken => cancellation.Token;
        internal bool CancellationRequested
        {
            get
            {
                lock (sync)
                {
                    return cancellationRequested;
                }
            }
        }
        /// <summary>Waits for the outcome. Execution failures throw; canceling this wait does not cancel the operation.</summary>
        public Task<NavigationResult> WaitAsync (CancellationToken waitCancellationToken = default)
        {
            if (ScreenWorkExecution.EntryId is NavigationEntryId owner && RemovedEntries.Contains(owner))
            {
                throw new InvalidOperationException("Owned work cannot await an operation that ends its own owner. Submit the operation and return from the work.");
            }
            return AsyncWait.WaitAsync(completion.Task, waitCancellationToken).AsTask();
        }

        internal Task<NavigationResult> WaitForRuntimeAsync () => completion.Task;

        internal static async Task ExecuteAsync (Func<NavigationOperation> request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NavigationOperation operation = request();
            using CancellationTokenRegistration registration = cancellationToken.Register(() => operation.TryRequestCancellation());
            NavigationResult result = await operation.WaitAsync();
            if (!result.DestinationCommitted)
            {
                throw new NavigationException(result.OperationId, result.Operation, false, result.FinalSnapshot,
                    result.Changes, result.Restoration, result.PresentationStatus, result.Diagnostics,
                    new InvalidOperationException(result.Diagnostics.FirstOrDefault()?.Reason ?? "The navigation request was not committed."));
            }
        }

        /// <summary>Requests cancellation before the irreversible boundary. Callback exceptions propagate after the request is accepted.</summary>
        public bool TryRequestCancellation ()
        {
            lock (sync)
            {
                if (completed || irrevocable || cancellationRequested)
                {
                    return false;
                }

                cancellationRequested = true;
            }

            // User cancellation callbacks never execute under the commit/admission lock.
            cancellation.Cancel();

            return true;
        }

        internal void ApplyCommit (Action apply)
        {
            lock (sync)
            {
                if (cancellationRequested)
                {
                    throw new OperationCanceledException(cancellation.Token);
                }

                apply();
                irrevocable = true;
            }
        }

        internal async Task ObserveAsync (ValueTask<NavigationResult> result)
        {
            try
            {
                NavigationResult value = await result;
                lock (sync)
                {
                    completed = true;
                    completion.TrySetResult(value with
                    {
                        OperationId = Id
                    });
                }
            }
            catch (OperationCanceledException exception)
            {
                lock (sync)
                {
                    completed = true;
                    completion.TrySetCanceled(exception.CancellationToken);
                }
            }
            catch (Exception exception)
            {
                lock (sync)
                {
                    completed = true;
                    completion.TrySetException(exception);
                }
            }
        }

        internal static NavigationOperation FromResult (NavigationResult result)
        {
            NavigationOperation operation = new();
            _ = operation.ObserveAsync(new ValueTask<NavigationResult>(result));
            return operation;
        }
    }
}
