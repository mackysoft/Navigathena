using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Acquires a value and ends the acquisition, including partial acquisition after failure.</summary>
    /// <remarks>
    /// The constructor must not acquire resources. The runtime owns this object before calling AcquireAsync, and never calls acquisition and release concurrently.
    /// It checks cancellation immediately before and after acquisition. Implementations forward the token to ongoing work without duplicating the entry check.
    /// ReleaseAsync is awaited even if cancellation prevents acquisition from starting, and must release any partially acquired resources.
    /// Release progress belongs to the operation ending the acquisition, not to the operation that acquired it. Failed release is not retried automatically.
    /// </remarks>
    public interface IResourceAcquisition<T>
    {
        ValueTask<T> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken);

        /// <summary>Ends this acquisition, including partial ownership, and optionally reports release progress. Cleanup is not canceled.</summary>
        ValueTask ReleaseAsync (NavigationProgressReporter progress);
    }
}
