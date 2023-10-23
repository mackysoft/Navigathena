using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

	public sealed class SceneHistory : IReadOnlyCollection<SceneHistoryEntry>
	{

		const int kDefauktCapacity = 8;

		readonly Stack<SceneHistoryEntry> m_Stack;

		public int Count => m_Stack.Count;

		public SceneHistory ()
		{
			m_Stack = new Stack<SceneHistoryEntry>(kDefauktCapacity);
		}

		public SceneHistory (IEnumerable<SceneHistoryEntry> entries)
		{
			if (entries == null)
			{
				throw new ArgumentNullException(nameof(entries));
			}
			m_Stack = new Stack<SceneHistoryEntry>(entries.Where(x => x != null));
		}

		public void Push (SceneHistoryEntry entry)
		{
			if (entry == null)
			{
				throw new ArgumentNullException(nameof(entry));
			}
			m_Stack.Push(entry);
		}

		public SceneHistoryEntry Pop () => m_Stack.Pop();

		public SceneHistoryEntry Peek () => m_Stack.Peek();

		public bool TryPop (out SceneHistoryEntry result) => m_Stack.TryPop(out result);

		public bool TryPeek (out SceneHistoryEntry result) => m_Stack.TryPeek(out result);

		public void Clear () => m_Stack.Clear();

		public IEnumerator<SceneHistoryEntry> GetEnumerator () => m_Stack.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator () => m_Stack.GetEnumerator();
		
	}

}