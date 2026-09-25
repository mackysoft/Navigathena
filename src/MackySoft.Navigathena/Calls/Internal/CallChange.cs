using System;

namespace MackySoft.Navigathena
{
    internal sealed record CallChange (Guid Id, CallChangeKind Kind)
    {
        internal CallEndReason EndReason
        {
            get; init;
        }
        internal NavigationEntryId? Owner
        {
            get; init;
        }
    }

    internal enum CallEndReason
    {
        Answer,
        Back,
        Close,
        Cancellation,
        Interrupted
    }

    internal enum CallChangeKind
    {
        Open,
        Replace,
        Reset,
        Close
    }
}
