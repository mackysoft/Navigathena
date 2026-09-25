namespace MackySoft.Navigathena
{
    /// <summary>Specifies a target region relative to the presentation that issued an action.</summary>
    public sealed class RegionTarget
    {
        private RegionTarget (RegionTargetKind kind, RegionDefinitionId? first, RegionDefinitionId? second)
        {
            Kind = kind;
            First = first;
            Second = second;
        }

        internal RegionTargetKind Kind
        {
            get;
        }
        internal RegionDefinitionId? First
        {
            get;
        }
        internal RegionDefinitionId? Second
        {
            get;
        }
        public static RegionTarget OwnRegion { get; } = new(RegionTargetKind.OwnRegion, null, null);
        public static RegionTarget Root { get; } = new(RegionTargetKind.Root, null, null);
        public static RegionTarget Child (RegionDefinitionId id) => new(RegionTargetKind.Child, id, null);
        public static RegionTarget AncestorRegion (RegionDefinitionId id) => new(RegionTargetKind.AncestorRegion, id, null);
        public static RegionTarget AncestorChild (RegionDefinitionId ownerRegionId, RegionDefinitionId childId) => new(RegionTargetKind.AncestorChild, ownerRegionId, childId);
    }

}
