namespace MackySoft.Navigathena.SceneManagement
{
	public interface ISceneHistoryBuilder
	{
		int Count { get; }
		SceneHistoryEntry this[int index] { get; set; }

		ISceneHistoryBuilder Add (SceneHistoryEntry entry);
		ISceneHistoryBuilder Insert(int index, SceneHistoryEntry entry);
		ISceneHistoryBuilder RemoveAt (int index);
		ISceneHistoryBuilder RemoveAllExceptCurrent ();
		void Build ();
	}
}