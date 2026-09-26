using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Connects native Unity acquisitions to a runtime-managed lifetime.</summary>
    public static class UnityLifetimeExtensions
    {
        public static async ValueTask<UnityScene<TView>> LoadSceneAsync<TView> (this LifetimeContext lifetime, IResourceAcquisition<Scene> acquisition, CancellationToken cancellationToken = default) where TView : Component
        {
            if (lifetime is null)
            {
                throw new ArgumentNullException(nameof(lifetime));
            }

            Scene scene = await lifetime.AcquireAsync(acquisition, cancellationToken);
            NativeResources.UnityThread.AssertCurrent();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new NavigationConfigurationException("The acquired scene is not loaded.");
            }

            TView[] roots = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TView>(true)).ToArray();
            if (roots.Length != 1)
            {
                throw new NavigationConfigurationException("The acquired scene must contain exactly one " + typeof(TView).FullName + "; found " + roots.Length + ".");
            }

            await lifetime.AcquireAsync(new NativeResources.ComponentObservation<TView>(roots[0]), cancellationToken);
            return new UnityScene<TView>(scene, roots[0]);
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

            return lifetime.AcquireAsync(new NativeResources.PrefabInstantiation<TView>(prefab), cancellationToken);
        }
    }
}
