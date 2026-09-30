using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.Runtime.Progress
{
    internal sealed class ProgressState<TState> : IDisposable
    {
        private readonly List<IDisposable> inputs = new();
        private readonly Queue<(long Version, TState Value, NavigationPhase Phase)> pending = new();
        private Subscription[] observers = Array.Empty<Subscription>();
        private Action<NavigationPhase, Exception>? reportFailure;
        private TState current;
        private long version;
        private NavigationPhase phase = NavigationPhase.Prepare;
        private bool delivering;
        private bool ended;

        internal ProgressState (NavigationProgressSink sink, TState initialState)
        {
            SyncRoot = sink.SyncRoot;
            reportFailure = sink.ReportFailure;
            current = initialState;
        }

        internal object SyncRoot { get; }
        internal TState Current
        {
            get
            {
                lock (SyncRoot)
                {
                    return current;
                }
            }
        }

        internal void AddInput (IDisposable subscription) => inputs.Add(subscription);

        internal void Apply<T> (ProgressUpdate<T> update, Func<TState, ProgressUpdate<T>, TState> reduce)
        {
            lock (SyncRoot)
            {
                if (ended)
                {
                    return;
                }
                current = reduce(current, update);
                phase = update.Phase;
                pending.Enqueue((++version, current, phase));
                if (delivering)
                {
                    return;
                }
                delivering = true;
                try
                {
                    // A receiver may report or register another receiver. Preserve snapshot order without recursive delivery.
                    while (pending.Count != 0)
                    {
                        (long sampleVersion, TState value, NavigationPhase samplePhase) = pending.Dequeue();
                        foreach (Subscription observer in observers)
                        {
                            Notify(observer, sampleVersion, value, samplePhase);
                        }
                    }
                }
                finally
                {
                    delivering = false;
                }
            }
        }

        internal IDisposable Observe (Action<TState> receive)
        {
            lock (SyncRoot)
            {
                if (ended)
                {
                    throw new InvalidOperationException("The transition's progress observation has ended.");
                }
                Subscription subscription = new(this, receive);
                Subscription[] next = new Subscription[observers.Length + 1];
                Array.Copy(observers, next, observers.Length);
                next[observers.Length] = subscription;
                observers = next;
                Notify(subscription, version, current, phase);
                return subscription;
            }
        }

        private void Notify (Subscription subscription, long sampleVersion, TState value, NavigationPhase samplePhase)
        {
            if (subscription.Receive is null || sampleVersion <= subscription.Version)
            {
                return;
            }
            subscription.Version = sampleVersion;
            try
            {
                subscription.Receive(value);
            }
            catch (Exception exception)
            {
                reportFailure?.Invoke(samplePhase, exception);
            }
        }

        public void Dispose ()
        {
            lock (SyncRoot)
            {
                if (ended)
                {
                    return;
                }
                ended = true;
                foreach (Subscription observer in observers)
                {
                    observer.Receive = null;
                }
                observers = Array.Empty<Subscription>();
                foreach (IDisposable input in inputs)
                {
                    input.Dispose();
                }
                inputs.Clear();
                pending.Clear();
                reportFailure = null;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly ProgressState<TState> state;

            internal Subscription (ProgressState<TState> state, Action<TState> receive)
            {
                this.state = state;
                Receive = receive;
            }

            internal Action<TState>? Receive { get; set; }
            internal long Version { get; set; } = -1;

            public void Dispose ()
            {
                lock (state.SyncRoot)
                {
                    Receive = null;
                }
            }
        }
    }
}
