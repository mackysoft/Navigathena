using System;

namespace MackySoft.Navigathena.SceneManagement
{
	public interface ISceneProgressFactory
	{
		IProgressDataStore CreateProgressDataStore ();
		IProgress<float> CreateProgress (IProgressDataStore dataStore, IProgress<IProgressDataStore> targetProgress);
	}
}