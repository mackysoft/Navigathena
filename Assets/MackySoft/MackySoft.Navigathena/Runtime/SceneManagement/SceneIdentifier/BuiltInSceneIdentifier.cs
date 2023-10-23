using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement
{

	public sealed class BuiltInSceneIdentifier : ISceneIdentifier
	{

		readonly string m_SceneName;

		public BuiltInSceneIdentifier (string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				throw new ArgumentException("Scene name cannot be null or empty.", nameof(sceneName));
			}
			m_SceneName = sceneName;
		}

		public ISceneHandle CreateHandle ()
		{
			return new BuiltInSceneHandle(m_SceneName);
		}

		public override string ToString ()
		{
			return $"{m_SceneName} ({nameof(BuiltInSceneIdentifier)})";
		}

		sealed class BuiltInSceneHandle : ISceneHandle
		{

			readonly string m_SceneName;

			public BuiltInSceneHandle (string sceneName)
			{
				m_SceneName = sceneName;
			}

			public async UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				// If the scene is already loaded, do nothing.
				if (SceneManager.GetSceneByName(m_SceneName).IsValid())
				{
					throw new InvalidOperationException($"Scene '{m_SceneName}' is already loaded.");
				}

				await SceneManager.LoadSceneAsync(m_SceneName, LoadSceneMode.Additive)
					.ToUniTask(progress, cancellationToken: cancellationToken);

				return SceneManager.GetSceneByName(m_SceneName);
			}

			public async UniTask Unload (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				Scene scene = SceneManager.GetSceneByName(m_SceneName);
				if (!scene.isLoaded && !scene.IsValid())
				{
					throw new InvalidOperationException("Scene is not loaded.");
				}

				await SceneManager.UnloadSceneAsync(m_SceneName)
					.ToUniTask(progress, cancellationToken: cancellationToken);
			}
		}
	}
}