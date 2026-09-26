using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

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
            handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(reference);
            GameObject asset = await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
            {
                throw handle.OperationException ?? new InvalidOperationException("Addressables completed without a prefab asset.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return asset;
        }

        public ValueTask DisposeAsync ()
        {
            UnityThread.AssertCurrent();
            if (handle.IsValid())
            {
                UnityEngine.AddressableAssets.Addressables.Release(handle);
                handle = default;
            }

            return default;
        }
    }
}
