using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Registration available only during the runtime-owned preparation callback.</summary>
    public abstract class ResourcePreparationContext
    {
        internal ResourcePreparationContext ()
        {
        }
        /// <summary>Registers ownership before calling a normal constructor factory. Prefers asynchronous disposal when available.</summary>
        public abstract T CreateOwned<T> (Func<T> create) where T : class;
        public abstract ValueTask<T> AcquireAsync<T> (IResourceAcquisition<T> acquisition, CancellationToken cancellationToken = default);
        public abstract ValueTask<T> BorrowAsync<T> (ResourceReference<T> resource, CancellationToken cancellationToken = default) where T : class;
    }
}
