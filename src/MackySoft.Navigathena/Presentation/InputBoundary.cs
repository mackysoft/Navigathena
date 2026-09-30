namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Identifies an eligible entry blocking lower entries in its region and their child screens.</summary>
    /// <param name="RegionId">The region whose lower history is affected, not a host-wide input plane.</param>
    /// <param name="OwnerEntryId">The boundary owner, not the first blocked entry.</param>
    public sealed record InputBoundary (RegionInstanceId RegionId, NavigationEntryId OwnerEntryId);

}
