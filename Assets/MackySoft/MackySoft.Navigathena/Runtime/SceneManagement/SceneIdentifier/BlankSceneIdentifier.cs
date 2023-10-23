using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement
{
	public sealed class BlankSceneIdentifier : ISceneIdentifier
	{

		readonly string m_SceneName;
		readonly Type m_EntryPointType;

		public BlankSceneIdentifier (string sceneName, Type entryPointType)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				throw new ArgumentException("Scene name cannot be null or empty.", nameof(sceneName));
			}
			if (entryPointType == null)
			{
				throw new ArgumentNullException(nameof(entryPointType));
			}
			if (!typeof(ISceneEntryPoint).IsAssignableFrom(entryPointType))
			{
				throw new ArgumentException($"'{entryPointType}' is not assignable from '{nameof(ISceneEntryPoint)}'.", nameof(entryPointType));
			}
			m_SceneName = sceneName;
			m_EntryPointType = entryPointType;
		}

		public static ISceneIdentifier Create<T> (string sceneName) where T : ISceneEntryPoint
		{
			return new BlankSceneIdentifier(sceneName, typeof(T));
		}

		public ISceneHandle CreateHandle ()
		{
			return new BlankSceneHandle(m_SceneName, m_EntryPointType);
		}

		public override string ToString ()
		{
			return $"{m_SceneName} ({nameof(BlankSceneIdentifier)})";
		}

		sealed class BlankSceneHandle : ISceneHandle
		{

			readonly string m_SceneName;
			readonly Type m_EntryPointType;

			public BlankSceneHandle (string sceneName, Type entryPointType)
			{
				m_SceneName = sceneName;
				m_EntryPointType = entryPointType;
			}

			public UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default)
			{
				// If the scene is already loaded, do nothing.
				if (SceneManager.GetSceneByName(m_SceneName).IsValid())
				{
					throw new InvalidOperationException($"Scene '{m_SceneName}' is already loaded.");
				}

				Scene newScene = SceneManager.CreateScene(m_SceneName);

				// Create entry point
				GameObject entryPointGameObject = new GameObject("SceneEntryPoint");
				entryPointGameObject.AddComponent(m_EntryPointType);
				SceneManager.MoveGameObjectToScene(entryPointGameObject, newScene);

				progress?.Report(1f);

				return UniTask.FromResult(newScene);
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