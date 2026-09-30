namespace MackySoft.Navigathena.Unity
{
    /// <summary>Typed inputs emitted by native Unity resource acquisitions. Include only the inputs needed by the application's display.</summary>
    public static class UnityProgress
    {
        public static readonly ProgressInput<SceneProgress> SceneLoading = new("unity.scene.loading");
        public static readonly ProgressInput<SceneProgress> SceneUnloading = new("unity.scene.unloading");
        /// <summary>Null while deferred destruction is pending, then one when the prefab instance is destroyed.</summary>
        public static readonly ProgressInput<double?> PrefabRelease = new("unity.prefab.release");
    }
}
