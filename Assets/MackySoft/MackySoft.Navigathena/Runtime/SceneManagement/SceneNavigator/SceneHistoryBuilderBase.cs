using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.SceneManagement
{
	public abstract class SceneHistoryBuilderBase : ISceneHistoryBuilder
	{

		protected readonly List<SceneHistoryEntry> m_History;

		public int Count => m_History.Count;

		public SceneHistoryEntry this[int index]
		{
			get => m_History[index];
			set {
				if (value == null)
				{
					throw new ArgumentNullException(nameof(value));
				}
				if ((index == 0) || (index < 0) || (index >= m_History.Count))
				{
					throw new ArgumentOutOfRangeException(nameof(index));
				}
				m_History[index] = value;
			}
		}

		protected SceneHistoryBuilderBase ()
		{
			m_History = new List<SceneHistoryEntry>();
		}

		protected SceneHistoryBuilderBase (IEnumerable<SceneHistoryEntry> history)
		{
			m_History = new List<SceneHistoryEntry>(history.Where(x => x != null));
		}

		public ISceneHistoryBuilder Add (SceneHistoryEntry entry)
		{
			if (entry == null)
			{
				throw new ArgumentNullException(nameof(entry));
			}
			m_History.Add(entry);
			return this;
		}

		public ISceneHistoryBuilder Insert(int index, SceneHistoryEntry entry)
		{
			if (entry == null)
			{
				throw new ArgumentNullException(nameof(entry));
			}
			if ((index == 0) || (index < 0) || (index > m_History.Count))
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			m_History.Insert(index, entry);
			return this;
		}

		public ISceneHistoryBuilder RemoveAt (int index)
		{
			if ((index == 0) || (index < 0) || (index >= m_History.Count))
			{
				throw new ArgumentOutOfRangeException(nameof(index));
			}
			m_History.RemoveAt(index);
			return this;
		}

		public ISceneHistoryBuilder RemoveAllExceptCurrent ()
		{
			if (m_History.Count > 1)
			{
				m_History.RemoveRange(1, m_History.Count - 1);
			}
			return this;
		}

		public abstract void Build ();
	}
}