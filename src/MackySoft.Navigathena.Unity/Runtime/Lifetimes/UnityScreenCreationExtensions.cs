using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Integration;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Acquires one configured screen and connects its presentation to the common runtime.</summary>
    public static class UnityScreenCreationExtensions
    {
        /// <summary>Acquires a view through the supplied implementation and connects its configured screen presentation.</summary>
        /// <param name="acquisition">Acquires the view and releases its ownership, including partial acquisition after failure.</param>
        /// <returns>The acquired view, connected to the screen lifetime. Entry animation and activity have not started.</returns>
        public static async ValueTask<TView> AcquireScreenAsync<TView> (this ScreenCreationContext creation, IResourceAcquisition<TView> acquisition, CancellationToken cancellationToken = default) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            TView view = await creation.Lifetime.AcquireAsync(acquisition, cancellationToken);
            await creation.Lifetime.AcquireAsync(new ComponentObservation<TView>(view), cancellationToken);
            return creation.ConnectScreen(view);
        }

        /// <summary>Acquires an independent instance of a configured screen prefab and connects its presentation.</summary>
        /// <param name="prefab">The view on the prefab root. The prefab remains externally owned; the screen owns the instance.</param>
        public static async ValueTask<TView> AcquireScreenAsync<TView> (this ScreenCreationContext creation, TView prefab, CancellationToken cancellationToken = default) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            UnityThread.AssertCurrent();
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }
            if (prefab.transform.parent != null)
            {
                throw new NavigationConfigurationException("The screen prefab reference must identify its root component.");
            }
            RequirePresentation(prefab).ValidateConfiguration();
            TView view = await creation.Lifetime.InstantiatePrefabAsync<TView>(prefab.gameObject, cancellationToken);
            return creation.ConnectScreen(view);
        }

        /// <summary>Acquires a prefab asset, creates a screen instance, and connects its presentation.</summary>
        /// <param name="prefab">Acquires the prefab asset, not a live screen. The instance is destroyed before the asset is released.</param>
        public static async ValueTask<TView> AcquireScreenAsync<TView> (this ScreenCreationContext creation, IResourceAcquisition<GameObject> prefab, CancellationToken cancellationToken = default) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            GameObject asset = await creation.Lifetime.AcquireAsync(prefab, cancellationToken);
            TView view = asset.GetComponent<TView>();
            if (view == null || asset.GetComponentsInChildren<TView>(true).Length != 1)
            {
                throw new NavigationConfigurationException("The screen prefab must have exactly one requested view, on its root.");
            }
            return await creation.AcquireScreenAsync(view, cancellationToken);
        }

        /// <summary>Acquires a scene through the supplied implementation and connects its uniquely resolved screen view.</summary>
        /// <param name="scene">Owns the acquired scene. Loading or creating it is the acquisition implementation's responsibility.</param>
        public static async ValueTask<TView> AcquireScreenAsync<TView> (this ScreenCreationContext creation, IResourceAcquisition<Scene> scene, CancellationToken cancellationToken = default) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            UnityScene<TView> acquired = await creation.Lifetime.AcquireSceneAsync<TView>(scene, cancellationToken);
            return creation.ConnectScreen(acquired.Root);
        }

        /// <summary>Acquires an owned scene and explicitly selects a screen when the scene contains multiple views.</summary>
        public static async ValueTask<TView> AcquireScreenAsync<TView> (this ScreenCreationContext creation, IResourceAcquisition<Scene> scene, Func<Scene, TView> select, CancellationToken cancellationToken = default) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            UnityScene<TView> acquired = await creation.Lifetime.AcquireSceneAsync(scene, select, cancellationToken);
            return creation.ConnectScreen(acquired.Root);
        }

        public static async ValueTask<TView> BorrowScreenAsync<TView> (this ScreenCreationContext creation, ResourceReference<TView> screen, ScreenAnimationState returnState, CancellationToken cancellationToken = default) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            TView view = await creation.Lifetime.BorrowAsync(screen, cancellationToken);
            await creation.Lifetime.AcquireAsync(new ComponentObservation<TView>(view), cancellationToken);
            return creation.ConnectScreen(view, returnState);
        }

        /// <summary>Connects a view already acquired or borrowed through this creation's resources; does not acquire or destroy it.</summary>
        public static TView ConnectScreen<TView> (this ScreenCreationContext creation, TView view, ScreenAnimationState? returnState = null) where TView : Component
        {
            if (creation is null)
            {
                throw new ArgumentNullException(nameof(creation));
            }
            UnityThread.AssertCurrent();
            creation.ConnectPresentation(RequirePresentation(view).CreateBinding(returnState));
            return view;
        }

        private static ScreenPresentation RequirePresentation (Component view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }
            ScreenPresentation presentation = view.GetComponent<ScreenPresentation>();
            return presentation != null ? presentation : throw new NavigationConfigurationException("The screen view must have ScreenPresentation on the same GameObject.");
        }
    }
}
