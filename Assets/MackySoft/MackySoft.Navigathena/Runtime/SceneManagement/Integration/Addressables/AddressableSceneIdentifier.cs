#if ENABLE_NAVIGATHENA_ADDRESSABLES
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement.AddressableAssets
{
	public sealed class AddressableSceneIdentifier : ISceneIdentifier
	{

		readonly object m_Key;

		public AddressableSceneIdentifier (object key)
		{
			if (key == null)
			{
				throw new ArgumentNullException(nameof(key));
			}
			m_Key = key;
		}

		public ISceneHandle CreateHandle ()
		{
			return new AddressableSceneHandle(m_Key);
		}

		public override string ToString ()
		{
			return $"{m_Key} ({nameof(AddressableSceneIdentifier)})";
		}

		sealed class AddressableSceneHandle : ISceneHandle
		{

			readonly object m_Key;
			AsyncOperationHandle<SceneInstance>? m_OperationHandle;

			public AddressableSceneHandle (object key)
			{
				m_Key = key;
			}

			public async UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				if (m_OperationHandle != null)
				{
					throw new InvalidOperationException($@"Scene ""{m_Key}"" is already loaded.");
				}

				progress?.Report(0f);

				AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(m_Key, LoadSceneMode.Additive);

				var sceneInstance = await handle.ToUniTask(progress, cancellationToken: cancellationToken);

				if (!sceneInstance.Scene.isLoaded)
				{
					await sceneInstance.ActivateAsync().ToUniTask(progress, cancellationToken: cancellationToken);
				}

				progress?.Report(1f);

				m_OperationHandle = handle;
				return sceneInstance.Scene;
			}

			public async UniTask Unload (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				if (m_OperationHandle == null)
				{
					throw new InvalidOperationException($@"Scene ""{m_Key}"" is not loaded.");
				}

				progress?.Report(0f);

				await Addressables.UnloadSceneAsync(m_OperationHandle.Value, true)
					.ToUniTask(progress, cancellationToken: cancellationToken);

				progress?.Report(1f);

				m_OperationHandle = null;
			}
		}
	}
}
#endif