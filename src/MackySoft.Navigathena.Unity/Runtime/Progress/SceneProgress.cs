namespace MackySoft.Navigathena.Unity
{
    /// <summary>A native scene operation's measured progress, independent of screen initialization and navigation completion.</summary>
    public readonly struct SceneProgress
    {
        public SceneProgress (string scenePath, float fraction, bool isDone)
        {
            ScenePath = scenePath;
            Fraction = fraction;
            IsDone = isDone;
        }

        public string ScenePath { get; }
        /// <summary>The AsyncOperation.progress value; this is not a byte-transfer or whole-navigation fraction.</summary>
        public float Fraction { get; }
        public bool IsDone { get; }
    }
}
