namespace MackySoft.Navigathena
{

    /// <summary>Reports a sequence-numbered checkpoint without defining an operation-wide fraction.</summary>
    public sealed record NavigationProgress (NavigationOperationId OperationId, long Sequence, NavigationPhase Phase, double? PhaseFraction);

}
