using System.Collections.Generic;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Provides ordered presentations and region-local input boundaries derived from a navigation state.</summary>
    public sealed class EffectiveComposition
    {
        internal EffectiveComposition (IReadOnlyList<EffectivePresentation> presentations, IReadOnlyList<InputBoundary> inputBoundaries)
        {
            Presentations = presentations;
            InputBoundaries = inputBoundaries;
        }

        public IReadOnlyList<EffectivePresentation> Presentations
        {
            get;
        }
        public IReadOnlyList<InputBoundary> InputBoundaries
        {
            get;
        }
    }

}
