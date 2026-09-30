using System;
using System.Collections.Generic;
using MackySoft.Navigathena.Presentation;

namespace MackySoft.Navigathena.Runtime.Execution
{
    internal sealed class RuntimeProgressReporter : NavigationProgressSink, INavigationOperationProgressReporter
    {
        private readonly NavigationOperationId operationId;
        private readonly IProgress<NavigationProgress>? receiver;
        // Only channel identities are erased; typed values never pass through object.
        private readonly Dictionary<object, object> channels = new();
        private readonly OperationDiagnostics diagnostics;
        private long sequence;

        public RuntimeProgressReporter (NavigationOperationId operationId, IProgress<NavigationProgress>? receiver, OperationDiagnostics diagnostics)
        {
            this.operationId = operationId;
            this.receiver = receiver;
            this.diagnostics = diagnostics;
        }

        public NavigationProgressReporter CreateWork (NavigationPhase phase, NavigationEntryId? entryId, string name)
            => new(this, phase, entryId, name);

        public override IDisposable Subscribe<T> (ProgressInput<T> input, Action<NavigationProgressReporter, ProgressUpdate<T>> receive)
        {
            lock (SyncRoot)
            {
                if (!channels.TryGetValue(input, out object? value))
                {
                    value = new Channel<T>(this, input);
                    channels.Add(input, value);
                }
                return ((Channel<T>)value).Subscribe(receive);
            }
        }

        public override void Report<T> (NavigationProgressReporter work, ProgressInput<T> input, T value)
        {
            lock (SyncRoot)
            {
                if (channels.TryGetValue(input, out object? channel))
                {
                    ((Channel<T>)channel).Report(work, new ProgressUpdate<T>(operationId, ++sequence, work, value));
                }
            }
        }

        public override void ReportFailure (NavigationPhase phase, Exception exception)
        {
            diagnostics.Add(phase, "A navigation progress callback threw an exception: " + exception.Message, exception);
        }

        public void Report (NavigationPhase phase, double? phaseFraction)
        {
            lock (SyncRoot)
            {
                if (phaseFraction.HasValue && (double.IsNaN(phaseFraction.Value) || double.IsInfinity(phaseFraction.Value) || phaseFraction.Value < 0d || phaseFraction.Value > 1d))
                {
                    diagnostics.Add(phase, "A progress report contained a non-finite or out-of-range phase fraction.", new ArgumentOutOfRangeException(nameof(phaseFraction)));
                    return;
                }
                try
                {
                    receiver?.Report(new NavigationProgress(operationId, ++sequence, phase, phaseFraction));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(phase, "A navigation progress receiver threw an exception: " + exception.Message, exception);
                }
            }
        }

        private sealed class Channel<T>
        {
            private readonly RuntimeProgressReporter owner;
            private readonly ProgressInput<T> input;
            private Subscription[] subscriptions = Array.Empty<Subscription>();

            internal Channel (RuntimeProgressReporter owner, ProgressInput<T> input)
            {
                this.owner = owner;
                this.input = input;
            }

            internal IDisposable Subscribe (Action<NavigationProgressReporter, ProgressUpdate<T>> receive)
            {
                Subscription subscription = new(this, receive);
                Subscription[] next = new Subscription[subscriptions.Length + 1];
                Array.Copy(subscriptions, next, subscriptions.Length);
                next[subscriptions.Length] = subscription;
                subscriptions = next;
                return subscription;
            }

            internal void Report (NavigationProgressReporter work, ProgressUpdate<T> update)
            {
                foreach (Subscription subscription in subscriptions)
                {
                    try
                    {
                        subscription.Receive?.Invoke(work, update);
                    }
                    catch (Exception exception)
                    {
                        owner.ReportFailure(work.Phase, exception);
                    }
                }
            }

            private sealed class Subscription : IDisposable
            {
                private readonly Channel<T> channel;

                internal Subscription (Channel<T> channel, Action<NavigationProgressReporter, ProgressUpdate<T>> receive)
                {
                    this.channel = channel;
                    Receive = receive;
                }

                internal Action<NavigationProgressReporter, ProgressUpdate<T>>? Receive { get; private set; }

                public void Dispose ()
                {
                    lock (channel.owner.SyncRoot)
                    {
                        if (Receive is null)
                        {
                            return;
                        }
                        Receive = null;
                        int index = Array.IndexOf(channel.subscriptions, this);
                        Subscription[] next = new Subscription[channel.subscriptions.Length - 1];
                        Array.Copy(channel.subscriptions, 0, next, 0, index);
                        Array.Copy(channel.subscriptions, index + 1, next, index, next.Length - index);
                        channel.subscriptions = next;
                        if (next.Length == 0)
                        {
                            channel.owner.channels.Remove(channel.input);
                        }
                    }
                }
            }
        }
    }
}
