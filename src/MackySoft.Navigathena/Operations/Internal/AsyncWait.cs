using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    internal static class AsyncWait
    {
        // Combinators can dispatch signal completion through a worker thread. Await
        // bridges keep their inputs' completion in the captured execution environment.
        public static async Task WaitAsync (Task operation) => await operation;
        public static async Task<T> WaitAsync<T> (Task<T> operation) => await operation;

        public static Task WhenAll (IEnumerable<Task> operations) => Task.WhenAll(operations.Select(WaitAsync));

        public static async Task<Task> WhenAny (Task first, Task second)
        {
            Task firstCompleted = ObserveCompletionAsync(first);
            Task secondCompleted = ObserveCompletionAsync(second);
            return await Task.WhenAny(firstCompleted, secondCompleted) == firstCompleted ? first : second;
        }

        private static async Task ObserveCompletionAsync (Task operation)
        {
            try
            {
                await operation;
            }
            catch
            {
                // WhenAny selects completion; the caller observes the original result.
            }
        }

        public static async ValueTask<T> WaitAsync<T> (Task<T> operation, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || operation.IsCompleted)
            {
                return await operation;
            }

            TaskCompletionSource<bool> cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(static value => ((TaskCompletionSource<bool>)value!).TrySetResult(true), cancelled);
            if (await WhenAny(operation, cancelled.Task) != operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await operation;
        }
    }
}
