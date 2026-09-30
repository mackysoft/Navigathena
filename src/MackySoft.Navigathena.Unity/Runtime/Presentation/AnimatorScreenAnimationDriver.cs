using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Plays finite states on an exclusively owned Animator. Configure full state paths, non-looping clips and no automatic transitions.</summary>
    [DisallowMultipleComponent]
    public sealed class AnimatorScreenAnimationDriver : MonoBehaviour, IScreenAnimator
    {
        [SerializeField] private Animator target = null!;
        [SerializeField] private int layer;
        [SerializeField] private string enter = "Base Layer.PushIn";
        [SerializeField] private string cover = "Base Layer.PushOut";
        [SerializeField] private string reveal = "Base Layer.PopIn";
        [SerializeField] private string exit = "Base Layer.PopOut";
        [SerializeField] private string beforeEnter = "Base Layer.BeforeEnter";
        [SerializeField] private string foreground = "Base Layer.Foreground";
        [SerializeField] private string background = "Base Layer.Background";
        [SerializeField] private string hidden = "Base Layer.Hidden";
        [SerializeField] private string afterExit = "Base Layer.AfterExit";
        [SerializeField, Min(0.01f)] private float timeoutSeconds = 30f;
        private bool playing;
        private int generation;

        private void Awake ()
        {
            if (target != null)
            {
                target.enabled = false;
            }
        }

        [ContextMenu("Validate Screen Animation")]
        public void ValidateConfiguration ()
        {
            if (target == null || target.runtimeAnimatorController == null || layer < 0 || layer >= target.layerCount
                || timeoutSeconds <= 0 || float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds))
            {
                throw new NavigationConfigurationException("Screen animation requires an Animator, a valid layer and a finite positive timeout.");
            }
            foreach (string state in new[] { enter, cover, reveal, exit, beforeEnter, foreground, background, hidden, afterExit })
            {
                if (string.IsNullOrWhiteSpace(state) || !target.HasState(layer, Animator.StringToHash(state)))
                {
                    throw new NavigationConfigurationException("Screen animation state is not available: " + state);
                }
            }
        }

        public void SetStateImmediately (ScreenAnimationState state)
        {
            UnityThread.AssertCurrent();
            if (playing)
            {
                throw new InvalidOperationException("Stop and await the screen animation before applying an immediate state.");
            }
            ValidateConfiguration();
            string pose = state switch
            {
                ScreenAnimationState.BeforeEnter => beforeEnter,
                ScreenAnimationState.Foreground => foreground,
                ScreenAnimationState.Background => background,
                ScreenAnimationState.Hidden => hidden,
                ScreenAnimationState.AfterExit => afterExit,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };
            try
            {
                StartState(pose);
            }
            finally
            {
                if (target != null)
                {
                    target.enabled = false;
                }
            }
        }

        public async ValueTask PlayAsync (ScreenAnimation animation, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            if (playing)
            {
                throw new InvalidOperationException("The screen already has a running animation.");
            }
            ValidateConfiguration();
            string state = animation.Kind switch
            {
                ScreenAnimationKind.Enter => enter,
                ScreenAnimationKind.Cover => cover,
                ScreenAnimationKind.Reveal => reveal,
                ScreenAnimationKind.Exit => exit,
                _ => throw new ArgumentOutOfRangeException(nameof(animation))
            };
            int playback = ++generation;
            playing = true;
            try
            {
                int hash = StartState(state);
                double deadline = Time.realtimeSinceStartupAsDouble + timeoutSeconds;
                while (true)
                {
                    if (this == null || !isActiveAndEnabled || target == null || !target.isActiveAndEnabled || generation != playback)
                    {
                        throw new InvalidOperationException("The screen animation target was disabled or destroyed.");
                    }
                    AnimatorStateInfo current = target.GetCurrentAnimatorStateInfo(layer);
                    if (current.fullPathHash != hash || target.IsInTransition(layer) || current.loop || current.speed * current.speedMultiplier * target.speed <= 0)
                    {
                        throw new NavigationConfigurationException("The screen animation was interrupted, loops, or cannot advance. Use finite states without automatic transitions.");
                    }
                    if (current.normalizedTime >= 1)
                    {
                        break;
                    }
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                    {
                        throw new TimeoutException("The screen animation did not finish within its configured timeout.");
                    }
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
            }
            finally
            {
                // No Animator writer remains after success, cancellation or failure.
                if (target != null)
                {
                    target.enabled = false;
                }
                playing = false;
            }
            SetStateImmediately(animation.To);
        }

        private int StartState (string state)
        {
            if (!isActiveAndEnabled || !target.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException("The screen animation must remain active until playback has stopped.");
            }
            target.updateMode = AnimatorUpdateMode.UnscaledTime;
            target.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            target.enabled = true;
            int hash = Animator.StringToHash(state);
            target.Play(hash, layer, 0);
            target.Update(0);
            if (target.IsInTransition(layer) || target.GetCurrentAnimatorStateInfo(layer).fullPathHash != hash)
            {
                throw new NavigationConfigurationException("The Animator could not enter the configured screen state.");
            }
            return hash;
        }

        private void OnDisable ()
        {
            generation++;
            if (target != null)
            {
                target.enabled = false;
            }
        }
    }
}
