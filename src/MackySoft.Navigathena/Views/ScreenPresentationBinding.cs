using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena
{
    /// <summary>Connects the presentation of one acquired screen without transferring resource ownership.</summary>
    /// <remarks>The acquisition must already belong to the creation resources, or be borrowed through them. A return state restores borrowed views at disconnection.</remarks>
    public sealed class ScreenPresentationBinding
    {
        public ScreenPresentationBinding (IReadOnlyList<IViewAdapter> views, IScreenAnimator? animator = null, ScreenAnimationState? returnState = null)
        {
            if (views is null)
            {
                throw new ArgumentNullException(nameof(views));
            }
            if (views.Count == 0 || views.Any(view => view is null))
            {
                throw new NavigationConfigurationException("A screen presentation requires at least one non-null view.");
            }
            if (returnState.HasValue && !Enum.IsDefined(typeof(ScreenAnimationState), returnState.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(returnState));
            }
            Views = Array.AsReadOnly(views.ToArray());
            Animator = animator;
            ReturnState = returnState;
        }

        public IReadOnlyList<IViewAdapter> Views
        {
            get;
        }
        public IScreenAnimator? Animator
        {
            get;
        }
        public ScreenAnimationState? ReturnState
        {
            get;
        }
    }
}
