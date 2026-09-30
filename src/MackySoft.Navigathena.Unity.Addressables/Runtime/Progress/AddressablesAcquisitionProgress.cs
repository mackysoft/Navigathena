using UnityEngine.ResourceManagement.AsyncOperations;

namespace MackySoft.Navigathena.Unity.Addressables
{
    /// <summary>Retains the asset identity, operation fraction and download counters without flattening their different meanings.</summary>
    public readonly struct AddressablesAcquisitionProgress
    {
        public AddressablesAcquisitionProgress (string assetGuid, float operationFraction, DownloadStatus download)
        {
            AssetGuid = assetGuid;
            OperationFraction = operationFraction;
            Download = download;
        }

        public string AssetGuid { get; }
        public float OperationFraction { get; }
        public DownloadStatus Download { get; }
    }
}
