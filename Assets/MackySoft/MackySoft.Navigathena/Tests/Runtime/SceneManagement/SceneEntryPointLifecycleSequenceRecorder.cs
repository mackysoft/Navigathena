using System;
using System.Collections.Generic;
using System.Linq;

namespace MackySoft.Navigathena.SceneManagement.Tests
{

	public interface ISceneEntryPointLifecycleAsserter
	{
		ISceneEntryPointLifecycleAsserter On (ISceneIdentifier identifier);
		ISceneEntryPointLifecycleAsserter Called (SceneEntryPointCallbackFlags flags);
		void SequenceEqual ();
	}

	public sealed class SceneEntryPointLifecycleSequenceRecorder
	{

		readonly List<(ISceneIdentifier identifier, SceneEntryPointCallbackFlags flags)> m_Sequence = new();

		public ISceneEntryPointLifecycleAsserter CreateSequenceAsserter ()
		{
			return new SceneEntryPointLifecycleAsserter(this);
		}

		public ISceneEntryPointLifecycleListener With (ISceneIdentifier identifier)
		{
			return new Listener(this, identifier);
		}

		sealed class Listener : ISceneEntryPointLifecycleListener
		{

			readonly SceneEntryPointLifecycleSequenceRecorder m_Assert;
			readonly ISceneIdentifier m_Identifier;

			public Listener (SceneEntryPointLifecycleSequenceRecorder assert, ISceneIdentifier identifier)
			{
				m_Assert = assert;
				m_Identifier = identifier;
			}

			void ISceneEntryPointLifecycleListener.OnReceive (SceneEntryPointCallbackFlags flags)
			{
				m_Assert.m_Sequence.Add((m_Identifier, flags));
			}
		}

		sealed class SceneEntryPointLifecycleAsserter : ISceneEntryPointLifecycleAsserter
		{

			readonly SceneEntryPointLifecycleSequenceRecorder m_Recorder;
			readonly List<(ISceneIdentifier identifier, SceneEntryPointCallbackFlags flags)> m_Sequence = new();

			ISceneIdentifier m_Current;

			public SceneEntryPointLifecycleAsserter (SceneEntryPointLifecycleSequenceRecorder recorder)
			{
				m_Recorder = recorder;
			}

			public ISceneEntryPointLifecycleAsserter On (ISceneIdentifier identifier)
			{
				m_Current = identifier;
				return this;
			}

			public ISceneEntryPointLifecycleAsserter Called (SceneEntryPointCallbackFlags flags)
			{
				m_Sequence.Add((m_Current, flags));
				return this;
			}

			public void SequenceEqual ()
			{
				if (m_Recorder.m_Sequence.Count != m_Sequence.Count)
				{
					throw new Exception($"Expected sequence length is {m_Sequence.Count} but actual is {m_Recorder.m_Sequence.Count}.\n{EnumerateActualSequence()}");
				}

				for (int i = 0; i < m_Sequence.Count; i++)
				{
					var actual = m_Recorder.m_Sequence[i];
					var expected = m_Sequence[i];

					if (actual.identifier != expected.identifier)
					{
						throw new Exception($"Expected identifier is {expected.identifier} but actual is {actual.identifier}.\n{EnumerateActualSequence()}");
					}

					if (actual.flags != expected.flags)
					{
						throw new Exception($"Expected flags is {expected.flags} but actual is {actual.flags}.\n{EnumerateActualSequence()}");
					}
				}
			}

			string EnumerateActualSequence ()
			{
				return string.Join("\n", m_Recorder.m_Sequence.Select(x => $"{x.identifier}: {x.flags}"));
			}
		}
	}
}