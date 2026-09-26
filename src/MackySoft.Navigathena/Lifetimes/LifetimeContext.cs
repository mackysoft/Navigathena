using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Registers owned objects, acquired resources, and borrowed references with a runtime-managed lifetime.</summary>
    /// <remarks>Registration is available only during the associated creation or preparation callback. The runtime ends ownership and borrowing after their users have stopped.</remarks>
    public abstract class LifetimeContext
    {
        internal LifetimeContext ()
        {
        }
        /// <summary>Registers ownership before calling a normal constructor factory. Prefers asynchronous disposal when available.</summary>
        public abstract T CreateOwned<T> (Func<T> create) where T : class;
        /// <summary>Owns an acquisition before starting it and awaits its disposal when this lifetime ends.</summary>
        public abstract ValueTask<T> AcquireAsync<T> (IResourceAcquisition<T> acquisition, CancellationToken cancellationToken = default);
        /// <summary>Registers use of an externally owned resource without taking responsibility for destroying it.</summary>
        public abstract ValueTask<T> BorrowAsync<T> (ResourceReference<T> resource, CancellationToken cancellationToken = default) where T : class;
    }
}
