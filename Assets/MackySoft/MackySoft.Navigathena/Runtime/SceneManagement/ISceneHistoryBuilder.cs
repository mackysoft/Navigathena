namespace MackySoft.Navigathena.SceneManagement
{
	public interface ISceneHistoryBuilder
	{
		int Count { get; }

		ISceneHistoryBuilder Add (SceneHistoryEntry entry);
		ISceneHistoryBuilder RemoveAt (int index);
		ISceneHistoryBuilder RemoveAllExceptCurrent ();
		void Build ();
	}
}