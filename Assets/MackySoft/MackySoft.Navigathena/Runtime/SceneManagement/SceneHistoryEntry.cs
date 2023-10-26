using System;
using MackySoft.Navigathena.Transitions;

namespace MackySoft.Navigathena.SceneManagement
{

	public interface IReadOnlySceneHistoryEntry
	{
		ISceneIdentifier Scene { get; }
		ITransitionDirector TransitionDirector { get; }
		ISceneDataReader DataReader { get; }
	}

	public sealed class SceneHistoryEntry : IReadOnlySceneHistoryEntry
	{
		public ISceneIdentifier Scene { get; }
		public ITransitionDirector TransitionDirector { get; }
		public SceneDataStore DataStore { get; }

		ISceneDataReader IReadOnlySceneHistoryEntry.DataReader => DataStore.Reader;

		public SceneHistoryEntry (ISceneIdentifier scene, ITransitionDirector transitionDirectorData, SceneDataStore sceneDataStore)
		{
			Scene = scene ?? throw new ArgumentNullException(nameof(scene));
			TransitionDirector = transitionDirectorData;
			DataStore = sceneDataStore ?? throw new ArgumentNullException(nameof(sceneDataStore));
		}
	}
}