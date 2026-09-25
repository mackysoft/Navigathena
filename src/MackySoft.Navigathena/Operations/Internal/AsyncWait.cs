using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    internal static class AsyncWait
    {
        public static async ValueTask<T> WaitAsync<T> (Task<T> operation, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || operation.IsCompleted)
            {
                return await operation;
            }

            TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(static value => ((TaskCompletionSource<bool>)value!).TrySetResult(true), cancelled);
            if (await Task.WhenAny(operation, cancelled.Task) != operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await operation;
        }
    }
}
