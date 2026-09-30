namespace MackySoft.Navigathena
{

    /// <summary>Receives applied logical changes in revision order.</summary>
    public interface INavigationCommitObserver
    {
        void OnCommitted (NavigationCommit commit);
    }

}
