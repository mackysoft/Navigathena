namespace MackySoft.Navigathena.Unity.Addressables
{
    /// <summary>Keeps Addressables operation progress and byte-transfer measurements distinct from native scene progress.</summary>
    public static class AddressablesProgress
    {
        public static readonly ProgressInput<AddressablesAcquisitionProgress> Acquisition = new("addressables.acquisition");
        /// <summary>The release operation's fraction, not a download fraction.</summary>
        public static readonly ProgressInput<double?> Release = new("addressables.release");
    }
}
