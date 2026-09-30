using MackySoft.Navigathena.Unity.Addressables;

namespace MackySoft.Navigathena.Samples.Startup
{
    public readonly struct StartupLoadingState
    {
        public StartupLoadingState (AddressablesAcquisitionProgress? scene, int? preparedChapter)
        {
            Scene = scene;
            PreparedChapter = preparedChapter;
        }

        public AddressablesAcquisitionProgress? Scene { get; }
        public int? PreparedChapter { get; }
    }
}
