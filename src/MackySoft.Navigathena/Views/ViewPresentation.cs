namespace MackySoft.Navigathena
{
    /// <summary>Runtime permissions, separate from the view's own opacity, enabled styling, and animation state.</summary>
    public readonly struct ViewPresentation
    {
        public ViewPresentation (bool outputEnabled, bool inputEnabled, int order)
        {
            OutputEnabled = outputEnabled;
            InputEnabled = inputEnabled;
            Order = order;
        }

        public bool OutputEnabled
        {
            get;
        }
        public bool InputEnabled
        {
            get;
        }
        public int Order
        {
            get;
        }
    }
}
