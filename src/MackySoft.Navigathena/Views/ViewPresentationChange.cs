using System;

namespace MackySoft.Navigathena
{
    /// <summary>Pairs one registered view with the presentation to apply.</summary>
    public readonly struct ViewPresentationChange
    {
        public ViewPresentationChange (IViewAdapter view, ViewPresentation presentation)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            Presentation = presentation;
        }

        public IViewAdapter View { get; }
        public ViewPresentation Presentation { get; }
    }
}
