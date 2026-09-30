using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Connects native Unity acquisitions to a runtime-managed lifetime.</summary>
    public static class UnityLifetimeExtensions
    {
        /// <summary>Acquires a scene and resolves its unique view without connecting a screen presentation.</summary>
        /// <param name="acquisition">Owns acquisition and release of the scene; it may load or create the scene.</param>
        public static ValueTask<UnityScene<TView>> AcquireSceneAsync<TView> (this LifetimeContext lifetime, IResourceAcquisition<Scene> acquisition, CancellationToken cancellationToken = default) where TView : Component
            => lifetime.AcquireSceneAsync(acquisition, SelectUniqueView<TView>, cancellationToken);

        /// <summary>Acquires a scene and resolves a selected view without connecting a screen presentation.</summary>
        /// <param name="select">Selects a live component belonging to the acquired scene.</param>
        public static async ValueTask<UnityScene<TView>> AcquireSceneAsync<TView> (this LifetimeContext lifetime, IResourceAcquisition<Scene> acquisition, Func<Scene, TView> select, CancellationToken cancellationToken = default) where TView : Component
        {
            if (lifetime is null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }
            if (select is null)
            {
                throw new ArgumentNullException(nameof(select));
            }

            Scene scene = await lifetime.AcquireAsync(acquisition, cancellationToken);
            UnityThread.AssertCurrent();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new NavigationConfigurationException("The acquired scene is not loaded.");
            }

            TView view = select(scene);
            if (view == null || view.gameObject.scene != scene)
            {
                throw new NavigationConfigurationException("The selected view must belong to the acquired scene.");
            }

            await lifetime.AcquireAsync(new ComponentObservation<TView>(view), cancellationToken);
            return new UnityScene<TView>(scene, view);
        }

        public static async ValueTask<TView> InstantiatePrefabAsync<TView> (this LifetimeContext lifetime, IResourceAcquisition<GameObject> asset, CancellationToken cancellationToken = default) where TView : Component
        {
            if (lifetime is null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            GameObject prefab = await lifetime.AcquireAsync(asset, cancellationToken);
            return await lifetime.InstantiatePrefabAsync<TView>(prefab, cancellationToken);
        }

        public static ValueTask<TView> InstantiatePrefabAsync<TView> (this LifetimeContext lifetime, GameObject prefab, CancellationToken cancellationToken = default) where TView : Component
        {
            if (lifetime is null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            return lifetime.AcquireAsync(new PrefabInstantiation<TView>(prefab), cancellationToken);
        }

        private static TView SelectUniqueView<TView> (Scene scene) where TView : Component
        {
            TView[] views = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TView>(true)).ToArray();
            if (views.Length != 1)
            {
                throw new NavigationConfigurationException("The acquired scene must contain exactly one " + typeof(TView).FullName + "; found " + views.Length + ".");
            }
            return views[0];
        }
    }
}
