using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Acquires a value and ends the acquisition, including partial acquisition after failure.</summary>
    /// <remarks>
    /// The constructor must not acquire resources. The runtime owns this object before calling AcquireAsync, and never calls acquisition and disposal concurrently.
    /// It checks cancellation immediately before and after acquisition. Implementations forward the token to ongoing work without duplicating the entry check.
    /// DisposeAsync is awaited even if cancellation prevents acquisition from starting, and must release any partially acquired resources.
    /// </remarks>
    public interface IResourceAcquisition<T> : IAsyncDisposable
    {
        ValueTask<T> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken);
    }
}
