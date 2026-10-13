using System;
using System.Threading;

namespace MackySoft.Navigathena
{
    /// <summary> Shares irreversible eligibility between token copies, independently of cancellation notification. </summary>
    internal sealed class ScreenForegroundPeriod : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private int revoked;

        public ScreenForegroundPeriod () => CancellationToken = cancellation.Token;

        public CancellationToken CancellationToken { get; }
        public bool IsValid => Volatile.Read(ref revoked) == 0;

        public void Invalidate () => Interlocked.Exchange(ref revoked, 1);

        public void NotifyRevocation () => cancellation.Cancel();

        public void Dispose () => cancellation.Dispose();
    }
}
