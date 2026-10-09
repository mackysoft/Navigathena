namespace MackySoft.Navigathena
{
    /// <summary>Identifies a view whose permission changes must be applied with its native event-dispatch batch.</summary>
    public interface IViewPresentationBatchAdapter : IViewAdapter
    {
        IViewPresentationBatch PresentationBatch { get; }
    }
}
