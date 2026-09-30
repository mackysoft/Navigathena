namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Specifies the state transition produced by a prepared publication.</summary>
    public enum CommitDisposition
    {
        Applied,
        Conflict,
        HostIncident,
    }

}
