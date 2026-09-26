using System;
using System.Linq;
using UnityEngine;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Inspector-authored presentation of one screen. Never locates a host or owns the screen lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class ScreenPresentation : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour[] views = Array.Empty<MonoBehaviour>();
        [SerializeField] private MonoBehaviour? animator;

        [ContextMenu("Validate Screen Presentation")]
        public void ValidateConfiguration ()
        {
            if (views.Length == 0 || views.Any(view => view == null || view is not IViewAdapter))
            {
                throw new NavigationConfigurationException("ScreenPresentation requires view components implementing IViewAdapter.");
            }
            if (views.Distinct().Count() != views.Length)
            {
                throw new NavigationConfigurationException("ScreenPresentation contains a duplicate view component.");
            }
            if (animator != null && animator is not IScreenAnimator)
            {
                throw new NavigationConfigurationException("The screen animation component must implement IScreenAnimator.");
            }
            if (views.Any(view => !view.transform.IsChildOf(transform)) || (animator != null && !animator.transform.IsChildOf(transform)))
            {
                throw new NavigationConfigurationException("Screen presentation components must belong to this screen hierarchy.");
            }
        }

        internal ScreenPresentationBinding CreateBinding (ScreenAnimationState? returnState)
        {
            ValidateConfiguration();
            IViewAdapter[] adapters = views.Cast<IViewAdapter>().ToArray();
            if (!returnState.HasValue && adapters.Any(view => view.Presentation.OutputEnabled || view.Presentation.InputEnabled))
            {
                throw new NavigationConfigurationException("A newly acquired screen must start with its output and input gates closed.");
            }
            return new ScreenPresentationBinding(adapters, animator as IScreenAnimator, returnState);
        }
    }
}
