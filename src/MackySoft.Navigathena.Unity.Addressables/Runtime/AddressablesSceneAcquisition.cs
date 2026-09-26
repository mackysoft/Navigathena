using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity.Addressables
{
    /// <summary>Loads one additive scene and releases its Addressables handle after managed users stop.</summary>
    public sealed class AddressablesSceneAcquisition : IResourceAcquisition<Scene>
    {
        private readonly AssetReference reference;
        private AsyncOperationHandle<SceneInstance> handle;
        private AsyncOperationHandle<SceneInstance> unload;
        private ResourceAcquisitionContext? context;
        private Scene scene;

        public AddressablesSceneAcquisition (AssetReference reference) => this.reference = reference ?? throw new ArgumentNullException(nameof(reference));

        public async ValueTask<Scene> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            this.context = context;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            handle = UnityEngine.AddressableAssets.Addressables.LoadSceneAsync(reference, LoadSceneMode.Additive, activateOnLoad: true);
            SceneInstance loaded = await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                throw handle.OperationException ?? new InvalidOperationException("Addressables scene acquisition failed.");
            }

            scene = loaded.Scene;
            cancellationToken.ThrowIfCancellationRequested();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException("Addressables completed without a loaded scene.");
            }

            return scene;
        }

        public async ValueTask DisposeAsync ()
        {
            UnityThread.AssertCurrent();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            context = null;
            if (!handle.IsValid())
            {
                return;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || !scene.IsValid() || !scene.isLoaded)
            {
                UnityEngine.AddressableAssets.Addressables.Release(handle);
                handle = default;
                return;
            }

            unload = UnityEngine.AddressableAssets.Addressables.UnloadSceneAsync(handle, UnloadSceneOptions.None, autoReleaseHandle: false);
            await unload.Task;
            if (unload.Status != AsyncOperationStatus.Succeeded)
            {
                throw unload.OperationException ?? new InvalidOperationException("Addressables scene termination failed.");
            }

            UnityEngine.AddressableAssets.Addressables.Release(unload);
            handle = default;
            unload = default;
        }

        private void OnSceneUnloaded (Scene unloaded)
        {
            if (unloaded == scene)
            {
                context?.ReportLoss("The acquired Addressables scene was unloaded outside its managed lifetime.");
            }
        }
    }
}
