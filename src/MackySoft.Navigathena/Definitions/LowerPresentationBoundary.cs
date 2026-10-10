namespace MackySoft.Navigathena
{
    /// <summary>Determines when a foreground screen establishes an input boundary, independently of lower activity.</summary>
    public enum LowerPresentationBoundary
    {
        /// <summary>Establishes a boundary when the same region has visible lower screens.</summary>
        WhenLowerVisible,
        /// <summary>Establishes a boundary even when the region has no visible lower history.</summary>
        /// <remarks>A registered or default blocker can therefore shield the application's surrounding content.</remarks>
        Required,
    }
}
