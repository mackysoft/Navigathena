using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

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
            handle = AddressablesApi.LoadSceneAsync(reference, LoadSceneMode.Additive, activateOnLoad: true);
            IProgress<AddressablesAcquisitionProgress> reports = context.Progress.GetReporter(AddressablesProgress.Acquisition);
            while (!handle.IsDone)
            {
                reports.Report(new AddressablesAcquisitionProgress(reference.AssetGUID, handle.PercentComplete, handle.GetDownloadStatus()));
                await UnityFrame.NextAsync(CancellationToken.None);
            }
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                throw handle.OperationException ?? new InvalidOperationException("Addressables scene acquisition failed.");
            }

            scene = handle.Result.Scene;
            cancellationToken.ThrowIfCancellationRequested();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException("Addressables completed without a loaded scene.");
            }

            reports.Report(new AddressablesAcquisitionProgress(reference.AssetGUID, 1, handle.GetDownloadStatus()));
            return scene;
        }

        public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
        {
            UnityThread.AssertCurrent();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            context = null;
            if (!handle.IsValid())
            {
                return;
            }
            IProgress<double?> reports = progress.GetReporter(AddressablesProgress.Release);

            if (handle.Status != AsyncOperationStatus.Succeeded || !scene.IsValid() || !scene.isLoaded)
            {
                AddressablesApi.Release(handle);
                handle = default;
                reports.Report(1);
                return;
            }

            unload = AddressablesApi.UnloadSceneAsync(handle, UnloadSceneOptions.None, autoReleaseHandle: false);
            while (!unload.IsDone)
            {
                reports.Report(unload.PercentComplete);
                await UnityFrame.NextAsync(CancellationToken.None);
            }
            if (unload.Status != AsyncOperationStatus.Succeeded)
            {
                throw unload.OperationException ?? new InvalidOperationException("Addressables scene termination failed.");
            }

            AddressablesApi.Release(unload);
            handle = default;
            unload = default;
            reports.Report(1);
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
