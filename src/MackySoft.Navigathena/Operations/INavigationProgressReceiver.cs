namespace MackySoft.Navigathena
{

    /// <summary>Receives progress emitted by the runtime for an operation.</summary>
    public interface INavigationProgressReceiver
    {
        void Report (NavigationProgress progress);
    }

}
