using System;

namespace MackySoft.Navigathena.SceneManagement
{
	public readonly struct SceneState
	{
		public ISceneIdentifier Identifier { get; }
		public ISceneHandle Handle { get; }
		public ISceneEntryPoint EntryPoint { get; }

		public SceneState (ISceneIdentifier identifier, ISceneHandle handle, ISceneEntryPoint entryPoint)
		{
			Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
			Handle = handle ?? throw new ArgumentNullException(nameof(handle));
			EntryPoint = entryPoint ?? throw new ArgumentNullException(nameof(entryPoint));
		}
	}
}