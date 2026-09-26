using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Lifetimes
{
    /// <summary>Acquires and borrows resources during a runtime-owned preparation callback.</summary>
    internal sealed class ManagedLifetimeContext : LifetimeContext
    {
        private readonly ResourceScope scope;
        internal ManagedLifetimeContext (ResourceScope scope) => this.scope = scope;

        public override T CreateOwned<T> (Func<T> create) => scope.CreateOwned(create);

        public override ValueTask<T> AcquireAsync<T> (IResourceAcquisition<T> acquisition, CancellationToken cancellationToken = default) => scope.AcquireAsync(acquisition, cancellationToken);

        public override ValueTask<T> BorrowAsync<T> (ResourceReference<T> resource, CancellationToken cancellationToken = default)
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<T>(scope.Borrow(resource));
        }
    }
}
