using System;

namespace MackySoft.Navigathena
{
    /// <summary>A typed report from one invocation within a navigation operation. Reporting completion does not complete that operation.</summary>
    public readonly struct ProgressUpdate<T>
    {
        internal ProgressUpdate (NavigationOperationId operationId, long sequence, NavigationProgressReporter work, T value)
        {
            OperationId = operationId;
            Sequence = sequence;
            Phase = work.Phase;
            WorkId = work.WorkId;
            EntryId = work.EntryId;
            WorkName = work.WorkName;
            Value = value;
        }

        /// <summary>The navigation operation which owns this report.</summary>
        public NavigationOperationId OperationId { get; }
        /// <summary>The report's order within the operation. Gaps are allowed.</summary>
        public long Sequence { get; }
        /// <summary>The runtime phase containing the reporting callback.</summary>
        public NavigationPhase Phase { get; }
        /// <summary>Identifies an invocation, allowing a reducer to distinguish concurrent acquisitions and repeated preparations.</summary>
        public Guid WorkId { get; }
        /// <summary>The reporting screen's history entry, or null for work not owned by one entry.</summary>
        public NavigationEntryId? EntryId { get; }
        /// <summary>A diagnostic label for the callback; use WorkId for identity.</summary>
        public string WorkName { get; }
        /// <summary>The producer's unboxed, typed snapshot.</summary>
        public T Value { get; }
    }
}
