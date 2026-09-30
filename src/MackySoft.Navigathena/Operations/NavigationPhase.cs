namespace MackySoft.Navigathena
{

    /// <summary>Specifies the operation phase represented by progress or diagnostics.</summary>
    public enum NavigationPhase
    {
        Prepare,
        Departure,
        Commit,
        Complete,
        Restore,
        Cleanup,
    }

}
