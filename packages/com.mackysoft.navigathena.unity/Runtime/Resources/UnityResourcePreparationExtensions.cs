using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>Connects native Unity acquisitions to the common preparation ownership.</summary>
    public static class UnityResourcePreparationExtensions
    {
        public static async ValueTask<UnityScene<TView>> LoadSceneAsync<TView> (this ResourcePreparationContext preparation, IResourceAcquisition<Scene> acquisition, CancellationToken cancellationToken = default) where TView : Component
        {
            if (preparation is null)
            {
                throw new ArgumentNullException(nameof(preparation));
            }

            Scene scene = await preparation.AcquireAsync(acquisition, cancellationToken);
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

            await preparation.AcquireAsync(new NativeResources.ComponentObservation<TView>(roots[0]), cancellationToken);
            return new UnityScene<TView>(scene, roots[0]);
        }

        public static async ValueTask<TView> InstantiatePrefabAsync<TView> (this ResourcePreparationContext preparation, IResourceAcquisition<GameObject> asset, CancellationToken cancellationToken = default) where TView : Component
        {
            if (preparation is null)
            {
                throw new ArgumentNullException(nameof(preparation));
            }

            GameObject prefab = await preparation.AcquireAsync(asset, cancellationToken);
            return await preparation.InstantiatePrefabAsync<TView>(prefab, cancellationToken);
        }

        public static ValueTask<TView> InstantiatePrefabAsync<TView> (this ResourcePreparationContext preparation, GameObject prefab, CancellationToken cancellationToken = default) where TView : Component
        {
            if (preparation is null)
            {
                throw new ArgumentNullException(nameof(preparation));
            }

            return preparation.AcquireAsync(new NativeResources.PrefabInstantiation<TView>(prefab), cancellationToken);
        }
    }
}
