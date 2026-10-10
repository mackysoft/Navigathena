namespace MackySoft.Navigathena
{
    /// <summary>Runtime permissions, separate from the view's own opacity, enabled styling, and animation state.</summary>
    public readonly struct ViewPresentation
    {
        public ViewPresentation (bool outputEnabled, bool inputEnabled, int order, ViewInputMode inputMode = ViewInputMode.All)
        {
            OutputEnabled = outputEnabled;
            InputEnabled = inputEnabled;
            Order = order;
            InputMode = inputMode;
        }

        public bool OutputEnabled { get; }
        public bool InputEnabled { get; }
        public int Order { get; }
        /// <summary>The input channels assigned to this presentation by its registration.</summary>
        public ViewInputMode InputMode { get; }
    }
}
