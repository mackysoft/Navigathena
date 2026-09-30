using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Runtime.Navigation;

namespace MackySoft.Navigathena
{
    /// <summary>A continuation owned by a screen instance's current history binding. Stopping activity does not cancel it; ending that binding does.</summary>
    /// <remarks>Completion releases the execution callback and cancellation resources. The handle keeps the result available for later waits.</remarks>
    public sealed class ScreenWork
    {
        private readonly Guid id;
        private readonly Task<object?> completion;
        private Action? cancel;
        internal ScreenWork (Guid id, Task<object?> completion, Action cancel)
        {
            this.id = id;
            this.completion = completion;
            this.cancel = cancel;
        }

        internal void DetachCancellation () => Interlocked.Exchange(ref cancel, null);

        /// <summary>Requests execution cancellation, or cancels pending work without invoking it. Completed work is unaffected.</summary>
        /// <remarks>Await <see cref="WaitAsync"/> to observe completion, including asynchronous cleanup in the callback.</remarks>
        /// <exception cref="AggregateException">A callback registered with the work's cancellation token throws.</exception>
        public void Cancel () => Volatile.Read(ref cancel)?.Invoke();

        /// <summary>Waits until the entire callback, including asynchronous finally blocks and code after InvokeAsync, has finished using screen resources.</summary>
        /// <param name="waitCancellationToken">Cancels only this wait, without canceling execution.</param>
        /// <returns>A task carrying the callback's success, failure, or cancellation after its resource use has ended.</returns>
        /// <exception cref="InvalidOperationException">A lifecycle callback or this work attempts to wait for the work.</exception>
        public Task WaitAsync (CancellationToken waitCancellationToken = default)
        {
            if (NavigationCallbackScope.IsExecuting || ScreenWorkExecution.WorkId == id)
            {
                throw new InvalidOperationException("A lifecycle callback or the work itself cannot wait for owned work to finish.");
            }
            return AsyncWait.WaitAsync(completion, waitCancellationToken).AsTask();
        }
    }
}
