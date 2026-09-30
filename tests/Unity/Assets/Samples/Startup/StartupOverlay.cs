using MackySoft.Navigathena.Unity.UGUI;
using UnityEngine;
using UnityEngine.UI;

namespace MackySoft.Navigathena.Samples.Startup
{
    public sealed class StartupOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasViewAdapter navigationView = null!;
        [SerializeField] private CanvasGroup opacity = null!;
        [SerializeField] private Text status = null!;
        [SerializeField] private Slider progress = null!;
        public IViewAdapter NavigationView => navigationView;
        public float Opacity
        {
            get => opacity.alpha;
            set => opacity.alpha = value;
        }

        public void Render (StartupLoadingState state)
        {
            if (state.PreparedChapter.HasValue)
            {
                status.text = "Chapter " + state.PreparedChapter.Value + " ready";
                progress.normalizedValue = 1;
            }
            else if (state.Scene.HasValue)
            {
                var scene = state.Scene.Value;
                if (!scene.Download.IsDone && scene.Download.TotalBytes > 0)
                {
                    status.text = "Downloading: " + scene.Download.DownloadedBytes + " / " + scene.Download.TotalBytes + " bytes";
                    progress.normalizedValue = scene.Download.Percent;
                }
                else
                {
                    status.text = "Loading scene";
                    progress.normalizedValue = scene.OperationFraction;
                }
            }
            else
            {
                status.text = "Preparing";
                progress.normalizedValue = 0;
            }
        }
    }
}
