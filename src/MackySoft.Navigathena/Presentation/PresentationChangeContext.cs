using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.Presentation
{
    /// <summary>Provides the reserved self and child-region changes for a retained presentation callback.</summary>
    public sealed class PresentationChangeContext
    {
        internal PresentationChangeContext (PresentationChange self, IReadOnlyList<ChildRegionChange> childChanges)
        {
            Self = self;
            ChildChanges = Array.AsReadOnly(childChanges.ToArray());
        }

        public PresentationChange Self
        {
            get;
        }
        public IReadOnlyList<ChildRegionChange> ChildChanges
        {
            get;
        }
    }
}
