using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace MackySoft.Navigathena.Unity.Addressables
{
    /// <summary>Acquires an Addressables prefab asset under common preparation ownership.</summary>
    public sealed class AddressablesAssetAcquisition : IResourceAcquisition<GameObject>
    {
        private readonly AssetReference reference;
        private AsyncOperationHandle<GameObject> handle;

        public AddressablesAssetAcquisition (AssetReference reference) => this.reference = reference ?? throw new ArgumentNullException(nameof(reference));

        public async ValueTask<GameObject> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            handle = AddressablesApi.LoadAssetAsync<GameObject>(reference);
            IProgress<AddressablesAcquisitionProgress> reports = context.Progress.GetReporter(AddressablesProgress.Acquisition);
            while (!handle.IsDone)
            {
                reports.Report(new AddressablesAcquisitionProgress(reference.AssetGUID, handle.PercentComplete, handle.GetDownloadStatus()));
                await UnityFrame.NextAsync(CancellationToken.None);
            }
            GameObject asset = handle.Result;
            if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
            {
                throw handle.OperationException ?? new InvalidOperationException("Addressables completed without a prefab asset.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            reports.Report(new AddressablesAcquisitionProgress(reference.AssetGUID, 1, handle.GetDownloadStatus()));
            return asset;
        }

        public ValueTask ReleaseAsync (NavigationProgressReporter progress)
        {
            UnityThread.AssertCurrent();
            if (handle.IsValid())
            {
                AddressablesApi.Release(handle);
                handle = default;
                progress.GetReporter(AddressablesProgress.Release).Report(1);
            }

            return default;
        }
    }
}
