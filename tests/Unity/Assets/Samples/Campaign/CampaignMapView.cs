using UnityEngine;
using UnityEngine.UI;

namespace MackySoft.Navigathena.Samples.Campaign
{
    public sealed class CampaignMapView : MonoBehaviour
    {
        [SerializeField] private Text chapterLabel = null!;
        [SerializeField] private Button nextChapter = null!;
        [SerializeField] private Button back = null!;

        public Button NextChapter => nextChapter;
        public Button Back => back;
        public void Render (string title) => chapterLabel.text = title;
    }
}
