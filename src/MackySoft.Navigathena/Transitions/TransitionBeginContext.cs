namespace MackySoft.Navigathena
{
    /// <summary>Identifies the planned operation before its destination is published.</summary>
    public sealed class TransitionBeginContext
    {
        internal TransitionBeginContext (NavigationOperationKind operation, NavigationState before, NavigationState after)
        {
            Operation = operation;
            Before = before;
            ProposedAfter = after;
        }

        public NavigationOperationKind Operation
        {
            get;
        }
        public NavigationState Before
        {
            get;
        }
        public NavigationState ProposedAfter
        {
            get;
        }
    }
}
