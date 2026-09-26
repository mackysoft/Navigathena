namespace MackySoft.Navigathena
{

    /// <summary>Notifies observers of exactly one applied logical history change.</summary>
    public sealed record NavigationCommit (long Revision, NavigationOperationId OperationId, NavigationDelta Changes, NavigationState State);

}
