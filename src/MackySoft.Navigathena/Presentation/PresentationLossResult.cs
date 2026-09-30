namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Reports whether a physical-loss group was applied, obsolete, or ended by an actual runtime closure.</summary>
    public enum PresentationLossResult
    {
        /// <summary>At least one current member was marked lost with one shared incident.</summary>
        Applied,

        /// <summary>No member was current when the open runtime processed the group, so state was unchanged.</summary>
        Obsolete,

        /// <summary>The runtime closed before it could accept or complete the loss report.</summary>
        RuntimeClosed,
    }

}
