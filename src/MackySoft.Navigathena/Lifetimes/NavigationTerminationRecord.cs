using System;
namespace MackySoft.Navigathena
{
    /// <summary>Identifies unfinished ownership without exposing its mutable native resources.</summary>
    public sealed record NavigationTerminationRecord (Guid Id, NavigationTerminationKind Kind, NavigationEntryId? ScreenEntryId, PresentationId? PresentationId, NavigationOperationId? OperationId, NavigationTerminationStatus Status, string? Reason)
    {
        public Exception? Exception
        {
            get; init;
        }
    }
}
