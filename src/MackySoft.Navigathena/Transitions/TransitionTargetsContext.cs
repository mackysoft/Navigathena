using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{
    public sealed class TransitionTargetsContext
    {
        internal TransitionTargetsContext (IReadOnlyList<TransitionScreenChange> changes)
        {
            Changes = Array.AsReadOnly(changes.ToArray());
        }

        public IReadOnlyList<TransitionScreenChange> Changes
        {
            get;
        }
    }
}
