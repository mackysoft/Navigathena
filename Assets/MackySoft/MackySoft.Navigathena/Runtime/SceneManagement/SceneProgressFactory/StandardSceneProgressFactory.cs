using System;

namespace MackySoft.Navigathena.SceneManagement
{

	public readonly struct LoadSceneProgressData
	{

		public float Progress { get; }

		public LoadSceneProgressData (float progress)
		{
			Progress = progress;
		}
	}

	public sealed class StandardSceneProgressFactory : ISceneProgressFactory
	{
		public IProgressDataStore CreateProgressDataStore ()
		{
			return new ProgressDataStore<LoadSceneProgressData>();
		}

		public IProgress<float> CreateProgress (IProgressDataStore dataStore, IProgress<IProgressDataStore> targetProgress)
		{
			return new Progress(dataStore, targetProgress);
		}

		sealed class Progress : IProgress<float>
		{

			readonly IProgressDataStore m_DataStore;
			readonly IProgress<IProgressDataStore> m_TargetProgress;

			public Progress (IProgressDataStore dataStore, IProgress<IProgressDataStore> targetProgress)
			{
				m_DataStore = dataStore;
				m_TargetProgress = targetProgress;
			}

			public void Report (float value)
			{
				m_TargetProgress.Report(m_DataStore.SetData(new LoadSceneProgressData(value)));
			}
		}
	}
}