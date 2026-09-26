using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.State
{

    internal sealed class NavigationStateStore : INavigationStateSource
    {
        private readonly object sync = new();
        private readonly List<TaskCompletionSource<NavigationState>> waiters = new();
        private NavigationState current;
        private bool closed;

        public NavigationStateStore (NavigationState initial) => current = initial;

        public NavigationState Current
        {
            get
            {
                lock (sync)
                {
                    return current;
                }
            }
        }

        public ValueTask<NavigationState> WaitForChangeAsync (long observedRevision, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                if (observedRevision > current.Revision)
                {
                    throw new ArgumentOutOfRangeException(nameof(observedRevision));
                }

                if (observedRevision < current.Revision)
                {
                    return new ValueTask<NavigationState>(current);
                }

                if (closed)
                {
                    return new ValueTask<NavigationState>(Task.FromException<NavigationState>(new NavigationRuntimeClosedException()));
                }

                TaskCompletionSource<NavigationState> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Add(waiter);
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(static state => ((TaskCompletionSource<NavigationState>)state!).TrySetCanceled(), waiter);
                }

                return new ValueTask<NavigationState>(waiter.Task);
            }
        }

        public void Publish (NavigationState state)
        {
            TaskCompletionSource<NavigationState>[] waiters;
            lock (sync)
            {
                current = state;
                waiters = this.waiters.ToArray();
                this.waiters.Clear();
            }

            foreach (TaskCompletionSource<NavigationState> waiter in waiters)
            {
                waiter.TrySetResult(state);
            }
        }

        public void Close ()
        {
            TaskCompletionSource<NavigationState>[] waiters;
            lock (sync)
            {
                closed = true;
                waiters = this.waiters.ToArray();
                this.waiters.Clear();
            }

            foreach (TaskCompletionSource<NavigationState> waiter in waiters)
            {
                waiter.TrySetException(new NavigationRuntimeClosedException());
            }
        }
    }

}
