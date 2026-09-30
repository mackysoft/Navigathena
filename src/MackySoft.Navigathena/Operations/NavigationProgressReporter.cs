using System;
using System.Threading;

namespace MackySoft.Navigathena
{
    /// <summary>Reports values for the current construction, initialization, preparation, acquisition, termination, or release. The runtime assigns operation and work identifiers.</summary>
    /// <remarks>Reports after the owning callback finishes are ignored. Inputs without an observer are valid. This API does not schedule application work.</remarks>
    public sealed class NavigationProgressReporter
    {
        private NavigationProgressSink? sink;
        internal static NavigationProgressReporter None { get; } = new(null, NavigationPhase.Prepare, null, string.Empty);

        internal NavigationProgressReporter (NavigationProgressSink? sink, NavigationPhase phase, NavigationEntryId? entryId, string name)
        {
            this.sink = sink;
            Phase = phase;
            EntryId = entryId;
            WorkName = name;
        }

        internal Guid WorkId { get; } = Guid.NewGuid();
        internal NavigationEntryId? EntryId { get; }
        internal NavigationPhase Phase { get; }
        internal string WorkName { get; }
        internal bool IsOpen => Volatile.Read(ref sink) is not null;

        /// <summary>Connects a producer using the standard IProgress contract to this invocation's typed input.</summary>
        /// <remarks>Keep the returned reporter only for this callback. Values are delivered without boxing value-type payloads.</remarks>
        public IProgress<T> GetReporter<T> (ProgressInput<T> input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            return new InputReporter<T>(this, input);
        }

        private void Report<T> (ProgressInput<T> input, T value)
        {
            NavigationProgressSink? target = Volatile.Read(ref sink);
            if (target is null)
            {
                return;
            }
            lock (target.SyncRoot)
            {
                if (ReferenceEquals(sink, target))
                {
                    target.Report(this, input, value);
                }
            }
        }

        internal ProgressSource<TState> CreateSource<TState> (ProgressDefinition<TState> definition)
        {
            NavigationProgressSink target = Volatile.Read(ref sink) ?? throw new InvalidOperationException("The progress registration period has ended.");
            lock (target.SyncRoot)
            {
                if (!ReferenceEquals(sink, target))
                {
                    throw new InvalidOperationException("The progress registration period has ended.");
                }
                return definition.CreateSource(target);
            }
        }

        internal bool Owns<TState> (ProgressSource<TState> source) => ReferenceEquals(Volatile.Read(ref sink)?.SyncRoot, source.State.SyncRoot);

        internal NavigationProgressReporter CreateChild (string name)
        {
            return new NavigationProgressReporter(Volatile.Read(ref sink), Phase, EntryId, name);
        }

        internal void Close ()
        {
            NavigationProgressSink? target = Volatile.Read(ref sink);
            if (target is not null)
            {
                lock (target.SyncRoot)
                {
                    Volatile.Write(ref sink, null);
                }
            }
        }

        private sealed class InputReporter<T> : IProgress<T>
        {
            private readonly NavigationProgressReporter owner;
            private readonly ProgressInput<T> input;

            internal InputReporter (NavigationProgressReporter owner, ProgressInput<T> input)
            {
                this.owner = owner;
                this.input = input;
            }

            public void Report (T value) => owner.Report(input, value);
        }
    }
}
