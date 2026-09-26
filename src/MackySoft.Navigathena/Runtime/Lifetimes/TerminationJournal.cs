using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Lifetimes
{
    internal sealed class TerminationJournal : INavigationTerminationSource
    {
        private readonly object sync = new();
        private readonly Dictionary<Guid, NavigationTerminationRecord> records = new();
        private NavigationTerminationSnapshot current = new(0, Array.Empty<NavigationTerminationRecord>());
        private TaskCompletionSource<NavigationTerminationSnapshot> changed = NewChange();
        public NavigationTerminationSnapshot Current
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        public ValueTask<NavigationTerminationSnapshot> WaitForChangeAsync (long observedRevision, CancellationToken waitCancellationToken = default)
        {
            lock (sync)
            {
                return observedRevision < current.Revision ? new ValueTask<NavigationTerminationSnapshot>(current)
                    : AsyncWait.WaitAsync(changed.Task, waitCancellationToken);
            }
        }

        public Task Track (NavigationTerminationKind kind, NavigationEntryId? entry, PresentationId? presentation, NavigationOperationId? operation, Func<ValueTask> terminate)
        {
            NavigationTerminationRecord record = new(Guid.NewGuid(), kind, entry, presentation, operation, NavigationTerminationStatus.Pending, null);
            Update(record.Id, record);
            return CompleteAsync(record, terminate);
        }

        private async Task CompleteAsync (NavigationTerminationRecord record, Func<ValueTask> terminate)
        {
            try
            {
                await terminate();
                Update(record.Id, null);
            }
            catch (Exception exception)
            {
                Update(record.Id, record with
                {
                    Status = NavigationTerminationStatus.Failed,
                    Reason = exception.Message,
                    Exception = exception
                });
                throw;
            }
        }

        private void Update (Guid id, NavigationTerminationRecord? record)
        {
            TaskCompletionSource<NavigationTerminationSnapshot> notify;
            NavigationTerminationSnapshot snapshot;
            lock (sync)
            {
                if (record is null)
                {
                    records.Remove(id);
                }
                else
                {
                    records[id] = record;
                }

                current = snapshot = new NavigationTerminationSnapshot(current.Revision + 1, Array.AsReadOnly(records.Values.ToArray()));
                notify = changed;
                changed = NewChange();
            }
            notify.TrySetResult(snapshot);
        }
        private static TaskCompletionSource<NavigationTerminationSnapshot> NewChange () => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
