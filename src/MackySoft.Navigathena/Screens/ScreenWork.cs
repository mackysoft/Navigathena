using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>A runtime-owned continuation. Stopping activity does not cancel it; ending its screen binding does.</summary>
    public sealed class ScreenWork
    {
        private readonly Action cancel;
        private readonly Func<CancellationToken, Task> wait;
        internal ScreenWork (Func<CancellationToken, Task> wait, Action cancel)
        {
            this.wait = wait;
            this.cancel = cancel;
        }

        public void Cancel () => cancel();

        /// <summary>Waits for the entire callback, including code after InvokeAsync. Canceling this wait does not cancel the work.</summary>
        /// <exception cref="InvalidOperationException">A lifecycle callback or this work attempts to wait for the work.</exception>
        public Task WaitAsync (CancellationToken waitCancellationToken = default) => wait(waitCancellationToken);
    }
}
