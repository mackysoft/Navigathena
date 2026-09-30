using MackySoft.Navigathena.Samples.Campaign;
using MackySoft.Navigathena.Unity.Addressables;

namespace MackySoft.Navigathena.Samples.Startup
{
    public static class StartupProgress
    {
        public static readonly ProgressDefinition<StartupLoadingState> Definition = ProgressDefinition.Create(() => new StartupLoadingState())
            .Reduce(AddressablesProgress.Acquisition, (state, update) => new StartupLoadingState(update.Value, state.PreparedChapter))
            .Reduce(CampaignProgress.PreparedChapter, (state, update) => new StartupLoadingState(state.Scene, update.Value))
            .Build();
    }
}
