namespace MackySoft.Navigathena.SceneManagement.Tests
{
	public sealed class SceneEntryPointCallbackFlagsStore : ISceneEntryPointLifecycleListener
	{
		public SceneEntryPointCallbackFlags Value { get; private set; }

		void ISceneEntryPointLifecycleListener.OnReceive (SceneEntryPointCallbackFlags flags)
		{
			Value |= flags;
		}
	}
}