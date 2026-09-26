using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Transitions
{
    /// <summary>Cancels sibling animations on failure and joins every writer before recovery or release.</summary>
    internal static class ScreenAnimationBatch
    {
        public static async Task RunAsync (IReadOnlyList<Func<CancellationToken, Task>> animations, CancellationToken cancellationToken)
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            async Task RunOneAsync (Func<CancellationToken, Task> animate)
            {
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await animate(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                catch (Exception failure)
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (Exception cancellationFailure)
                    {
                        throw new AggregateException("Animation and sibling cancellation failed.", failure, cancellationFailure);
                    }
                    throw;
                }
            }
            await Task.WhenAll(animations.Select(RunOneAsync));
        }
    }
}
