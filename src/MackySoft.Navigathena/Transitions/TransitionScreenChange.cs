namespace MackySoft.Navigathena
{
    /// <summary>Describes one participant's output change without exposing its native view or lifecycle implementation.</summary>
    public sealed record TransitionScreenChange (NavigationEntryId EntryId, NavigationRoute Route, bool WasPresented, bool WillBePresented);
}
