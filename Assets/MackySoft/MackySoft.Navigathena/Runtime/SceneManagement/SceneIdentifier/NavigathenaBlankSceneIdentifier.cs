using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement
{
	internal sealed class NavigathenaBlankSceneIdentifier : ISceneIdentifier
	{

		public static readonly NavigathenaBlankSceneIdentifier Instance = new NavigathenaBlankSceneIdentifier();

		public ISceneHandle CreateHandle ()
		{
			return NavigathenaBlankSceneHandle.Instance;
		}

		sealed class NavigathenaBlankSceneHandle : ISceneHandle
		{

			public static readonly NavigathenaBlankSceneHandle Instance = new NavigathenaBlankSceneHandle();

			const string kSceneName = "Navigathena Blank";

			public UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				if (SceneManager.GetSceneByName(kSceneName).IsValid())
				{
					return UniTask.FromResult(SceneManager.GetSceneByName(kSceneName));
				}
				return UniTask.FromResult(SceneManager.CreateScene(kSceneName));
			}

			public async UniTask Unload (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				Scene scene = SceneManager.GetSceneByName(kSceneName);
				if (!scene.isLoaded && !scene.IsValid())
				{
					return;
				}

				await SceneManager.UnloadSceneAsync(kSceneName)
					.ToUniTask(progress, cancellationToken: cancellationToken);
			}
		}
	}
}