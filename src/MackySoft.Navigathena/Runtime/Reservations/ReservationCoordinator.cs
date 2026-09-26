using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Reservations
{

    internal sealed class ReservationCoordinator
    {
        private readonly object sync = new();
        private readonly List<ReservationLease> leases = new();

        internal Task? CaptureRelease ()
        {
            lock (sync)
            {
                return leases.Count == 0 ? null : Task.WhenAll(leases.Select(lease => lease.Released.Task));
            }
        }

        public bool TryAcquire (IReadOnlyCollection<RegionInstanceId> reads, IReadOnlyCollection<RegionInstanceId> writes, out ReservationLease? lease)
        {
            lock (sync)
            {
                foreach (ReservationLease existing in leases)
                {
                    if (Intersects(writes, existing.Writes) || Intersects(writes, existing.Reads) || Intersects(reads, existing.Writes))
                    {
                        lease = null;
                        return false;
                    }
                }

                lease = new ReservationLease(this, reads, writes);
                leases.Add(lease);
                return true;
            }
        }

        private void Release (ReservationLease lease)
        {
            lock (sync)
            {
                leases.Remove(lease);
                lease.Released.TrySetResult(null);
            }
        }

        private static bool Intersects (IEnumerable<RegionInstanceId> left, IEnumerable<RegionInstanceId> right) => left.Intersect(right).Any();

        internal sealed class ReservationLease : IDisposable
        {
            private readonly ReservationCoordinator owner;
            private int disposed;
            internal TaskCompletionSource<object?> Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal ReservationLease (ReservationCoordinator owner, IReadOnlyCollection<RegionInstanceId> reads, IReadOnlyCollection<RegionInstanceId> writes)
            {
                this.owner = owner;
                Reads = reads;
                Writes = writes;
            }

            public IReadOnlyCollection<RegionInstanceId> Reads
            {
                get;
            }
            public IReadOnlyCollection<RegionInstanceId> Writes
            {
                get;
            }

            public void Dispose ()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    owner.Release(this);
                }
            }
        }
    }

}
