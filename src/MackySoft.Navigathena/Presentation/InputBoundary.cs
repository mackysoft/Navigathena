namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Identifies a foreground entry's input boundary for its region and child screens.</summary>
    /// <remarks>The boundary is independent of activity continuation. A blocker is selected when lower output is preserved.</remarks>
    /// <param name="RegionId">The region whose lower history is affected, not a host-wide input plane.</param>
    /// <param name="OwnerEntryId">The boundary owner, not the first blocked entry.</param>
    public sealed record InputBoundary (RegionInstanceId RegionId, NavigationEntryId OwnerEntryId);

}
