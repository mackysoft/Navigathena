using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Synchronization
{
    internal sealed class AsyncGate
    {
        private readonly object sync = new();
        private readonly Queue<TaskCompletionSource<bool>> waiters = new();
        private bool held;

        public ValueTask WaitAsync (CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<bool> waiter;
            lock (sync)
            {
                if (!held)
                {
                    held = true;
                    return default;
                }

                // Complete the signal directly; awaiters use their captured context rather
                // than requiring a worker thread to release the gate.
                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Enqueue(waiter);
            }

            return WaitForTurnAsync(waiter, cancellationToken);
        }

        private static async ValueTask WaitForTurnAsync (TaskCompletionSource<bool> waiter, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            await waiter.Task;
        }

        public void Release ()
        {
            while (true)
            {
                TaskCompletionSource<bool> waiter;
                lock (sync)
                {
                    if (!held)
                    {
                        throw new InvalidOperationException("The gate is not held.");
                    }
                    if (waiters.Count == 0)
                    {
                        held = false;
                        return;
                    }

                    waiter = waiters.Dequeue();
                }

                // A cancelled waiter must not consume the turn of the next live waiter.
                if (waiter.TrySetResult(true))
                {
                    return;
                }
            }
        }
    }
}
