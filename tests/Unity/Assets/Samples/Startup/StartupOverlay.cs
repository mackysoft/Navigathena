using MackySoft.Navigathena.Unity.UGUI;
using UnityEngine;

namespace MackySoft.Navigathena.Samples.Startup
{
    public sealed class StartupOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasViewAdapter navigationView = null!;
        [SerializeField] private CanvasGroup opacity = null!;
        public IViewAdapter NavigationView => navigationView;
        public float Opacity
        {
            get => opacity.alpha; set => opacity.alpha = value;
        }
    }
}
