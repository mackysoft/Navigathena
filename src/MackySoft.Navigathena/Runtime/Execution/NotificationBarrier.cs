using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed class NotificationBarrier
    {
        private readonly object sync = new();
        private TaskCompletionSource<object?>? active;

        public void Enter ()
        {
            lock (sync)
            {
                active ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Exit ()
        {
            TaskCompletionSource<object?>? active;
            lock (sync)
            {
                active = this.active;
                this.active = null;
            }

            active?.TrySetResult(null);
        }

        public Task WaitAsync ()
        {
            lock (sync)
            {
                return active?.Task ?? Task.CompletedTask;
            }
        }
    }
}
