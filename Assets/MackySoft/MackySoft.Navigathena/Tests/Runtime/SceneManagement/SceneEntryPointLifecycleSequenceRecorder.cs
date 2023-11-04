using System;
using System.Collections.Generic;

namespace MackySoft.Navigathena.SceneManagement.Tests
{
	public sealed class SceneEntryPointLifecycleSequenceRecorder
	{

		readonly List<(ISceneIdentifier identifier, SceneEntryPointCallbackFlags flags)> m_Sequence = new();

		public void Assert (params (ISceneIdentifier identifier, SceneEntryPointCallbackFlags flags)[] expectedSequence)
		{
			if (m_Sequence.Count != expectedSequence.Length)
			{
				throw new Exception($"Expected sequence length is {expectedSequence.Length} but actual is {m_Sequence.Count}.");
			}

			for (int i = 0; i < m_Sequence.Count; i++)
			{
				var actual = m_Sequence[i];
				var expected = expectedSequence[i];

				if (actual.identifier != expected.identifier)
				{
					throw new Exception($"Expected identifier is {expected.identifier} but actual is {actual.identifier}.");
				}

				if (actual.flags != expected.flags)
				{
					throw new Exception($"Expected flags is {expected.flags} but actual is {actual.flags}.");
				}
			}
		}

		public ISceneEntryPointLifecycleListener With (ISceneIdentifier identifier)
		{
			return new Listener(this,identifier);
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
				m_Assert.m_Sequence.Add((m_Identifier,flags));
			}
		}
	}
}