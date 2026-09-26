using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Provides one immutable adapter update for departure, destination, or restoration.</summary>
    public sealed class PresentationUpdate
    {
        internal PresentationUpdate (PresentationUpdateKind kind, NavigationState before, NavigationState proposedAfter, EffectiveComposition beforeComposition, EffectiveComposition proposedComposition, IReadOnlyList<PresentationChange> changes, IReadOnlyList<PresentationChangeContext> retainedChanges)
        {
            Kind = kind;
            Before = before;
            ProposedAfter = proposedAfter;
            BeforeComposition = beforeComposition;
            ProposedComposition = proposedComposition;
            Changes = Array.AsReadOnly(changes.ToArray());
            RetainedChanges = Array.AsReadOnly(retainedChanges.ToArray());
        }

        public PresentationUpdateKind Kind
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
        public EffectiveComposition BeforeComposition
        {
            get;
        }
        public EffectiveComposition ProposedComposition
        {
            get;
        }
        public IReadOnlyList<PresentationChange> Changes
        {
            get;
        }
        public IReadOnlyList<PresentationChangeContext> RetainedChanges
        {
            get;
        }

    }

}
