namespace MackySoft.Navigathena.Presentation
{
    /// <summary> Correlates one immutable restoration value with the exact physical screen that supplied it. </summary>
    public sealed class PresentationStateCapture
    {
        /// <param name="entryId"> The history entry whose state was captured. </param>
        /// <param name="presentationId"> The physical screen that supplied the state. </param>
        /// <param name="value"> An immutable restoration value, or null to clear previously saved state. The value does not own physical resources. </param>
        public PresentationStateCapture (NavigationEntryId entryId, PresentationId presentationId, object? value)
        {
            EntryId = entryId;
            PresentationId = presentationId;
            Value = value;
        }

        public NavigationEntryId EntryId
        {
            get;
        }
        public PresentationId PresentationId
        {
            get;
        }
        public object? Value
        {
            get;
        }
    }
}
