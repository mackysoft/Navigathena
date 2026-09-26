namespace MackySoft.Navigathena
{
    public sealed class TransitionSettlementContext
    {
        internal TransitionSettlementContext (TransitionSettlementTarget target, bool destinationCommitted)
        {
            Target = target;
            DestinationCommitted = destinationCommitted;
        }

        public TransitionSettlementTarget Target
        {
            get;
        }
        public bool DestinationCommitted
        {
            get;
        }
    }
}
