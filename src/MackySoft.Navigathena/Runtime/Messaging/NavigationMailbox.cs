using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Messaging
{
    internal sealed class NavigationMailbox
    {
        private readonly object sync = new();
        private readonly Queue<NavigationMailboxItem> items = new();
        private TaskCompletionSource<bool>? available;
        private bool closed;

        public ValueTask<NavigationOutcome> Write (NavigationMailboxRequest request, NavigationState closedSnapshot)
        {
            return TryWrite(request)
                ? new ValueTask<NavigationOutcome>(request.Completion.Task)
                : new ValueTask<NavigationOutcome>(request.Closed(closedSnapshot));
        }

        public PresentationLossMailboxAdmission Write (PresentationLossMailboxRequest request)
        {
            return TryWrite(request)
                ? new PresentationLossMailboxAdmission(true, request.Completion.Task)
                : new PresentationLossMailboxAdmission(false, Task.FromResult(PresentationLossResult.RuntimeClosed));
        }

        public HostIncidentMailboxAdmission Write (HostIncidentMailboxRequest request)
        {
            return TryWrite(request)
                ? new HostIncidentMailboxAdmission(true, request.Completion.Task)
                : new HostIncidentMailboxAdmission(false, Task.FromResult(HostIncidentReportResult.RuntimeClosed));
        }

        public ValueTask<NavigationOutcome> Write (NavigationRecoveryMailboxRequest request, NavigationState closedSnapshot)
        {
            return TryWrite(request)
                ? new ValueTask<NavigationOutcome>(request.Completion.Task)
                : new ValueTask<NavigationOutcome>(request.Closed(closedSnapshot));
        }

        private bool TryWrite (NavigationMailboxItem item)
        {
            TaskCompletionSource<bool>? notify;
            lock (sync)
            {
                if (closed)
                {
                    return false;
                }

                items.Enqueue(item);
                notify = available;
                available = null;
            }

            // Publish the enqueue before notifying the reader outside the mailbox lock.
            notify?.TrySetResult(true);
            return true;
        }

        public async ValueTask<NavigationMailboxItem?> ReadAsync (CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TaskCompletionSource<bool> waiting;
                lock (sync)
                {
                    if (items.Count > 0)
                    {
                        return items.Dequeue();
                    }
                    if (closed)
                    {
                        return null;
                    }

                    // Completing this signal needs no worker thread. Awaiters resume
                    // asynchronously through their captured synchronization context.
                    waiting = available ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                using CancellationTokenRegistration registration = cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));
                try
                {
                    await waiting.Task;
                }
                finally
                {
                    lock (sync)
                    {
                        if (ReferenceEquals(available, waiting))
                        {
                            available = null;
                        }
                    }
                }
            }
        }

        public IReadOnlyList<NavigationMailboxItem> CloseAndDrain ()
        {
            TaskCompletionSource<bool>? notify;
            NavigationMailboxItem[] pending;
            lock (sync)
            {
                closed = true;
                pending = items.ToArray();
                items.Clear();
                notify = available;
                available = null;
            }

            notify?.TrySetResult(false);
            return pending;
        }
    }

}
