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
        private readonly SemaphoreSlim signal = new(0);
        private bool closed;

        public ValueTask<NavigationOutcome> Write (NavigationMailboxRequest request, NavigationState closedSnapshot)
        {
            lock (sync)
            {
                if (closed)
                {
                    return new ValueTask<NavigationOutcome>(request.Closed(closedSnapshot));
                }

                items.Enqueue(request);
                signal.Release();
                return new ValueTask<NavigationOutcome>(request.Completion.Task);
            }
        }

        public PresentationLossMailboxAdmission Write (PresentationLossMailboxRequest request)
        {
            lock (sync)
            {
                if (closed)
                {
                    return new PresentationLossMailboxAdmission(false, Task.FromResult(PresentationLossResult.RuntimeClosed));
                }

                items.Enqueue(request);
                signal.Release();
                return new PresentationLossMailboxAdmission(true, request.Completion.Task);
            }
        }

        public HostIncidentMailboxAdmission Write (HostIncidentMailboxRequest request)
        {
            lock (sync)
            {
                if (closed)
                {
                    return new HostIncidentMailboxAdmission(false, Task.FromResult(HostIncidentReportResult.RuntimeClosed));
                }

                items.Enqueue(request);
                signal.Release();
                return new HostIncidentMailboxAdmission(true, request.Completion.Task);
            }
        }

        public ValueTask<NavigationOutcome> Write (NavigationRecoveryMailboxRequest request, NavigationState closedSnapshot)
        {
            lock (sync)
            {
                if (closed)
                {
                    return new ValueTask<NavigationOutcome>(request.Closed(closedSnapshot));
                }

                items.Enqueue(request);
                signal.Release();
                return new ValueTask<NavigationOutcome>(request.Completion.Task);
            }
        }

        public async ValueTask<NavigationMailboxItem?> ReadAsync (CancellationToken cancellationToken)
        {
            await signal.WaitAsync(cancellationToken);
            lock (sync)
            {
                return items.Count > 0 ? items.Dequeue() : null;
            }
        }

        public IReadOnlyList<NavigationMailboxItem> CloseAndDrain ()
        {
            lock (sync)
            {
                closed = true;
                NavigationMailboxItem[] pending = items.ToArray();
                items.Clear();
                return pending;
            }
        }
    }

}
