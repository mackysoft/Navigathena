using System;

namespace MackySoft.Navigathena
{
    internal sealed record NavigationTransitionRule (NavigationOperationKind Operation, Type? Source, Type? Destination, NavigationTransition Transition)
    {
        public int Specificity => (Source is null ? 0 : 1) + (Destination is null ? 0 : 1);
    }
}
