namespace MackySoft.Navigathena
{
    /// <summary>Identifies the input channels used by a registered presentation.</summary>
    public enum ViewInputMode
    {
        /// <summary>Accepts pointer and non-position input when runtime permission is granted.</summary>
        All,
        /// <summary>Intercepts pointer input without becoming a keyboard or navigation destination.</summary>
        PointerOnly,
    }
}
