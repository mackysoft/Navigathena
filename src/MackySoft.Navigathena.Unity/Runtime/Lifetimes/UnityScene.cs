using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.Unity
{
    /// <summary>The acquired scene and its uniquely resolved view; lifetime remains owned by Navigathena.</summary>
    public sealed class UnityScene<TView> where TView : Component
    {
        internal UnityScene (Scene scene, TView root)
        {
            Scene = scene;
            Root = root;
        }

        public Scene Scene
        {
            get;
        }
        public TView Root
        {
            get;
        }
    }
}
