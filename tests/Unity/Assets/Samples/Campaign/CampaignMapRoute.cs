namespace MackySoft.Navigathena.Samples.Campaign
{
    public sealed record CampaignMapRoute : Route
    {
        public CampaignMapRoute (int chapterId) => ChapterId = chapterId;
        public int ChapterId
        {
            get;
        }
    }
}
