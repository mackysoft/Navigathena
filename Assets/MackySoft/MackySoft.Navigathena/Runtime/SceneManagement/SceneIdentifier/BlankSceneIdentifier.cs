using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement
{
	public sealed class BlankSceneIdentifier<T> : ISceneIdentifier where T : Component, ISceneEntryPoint
	{

		readonly string m_SceneName;
		readonly Action<T> m_OnCreate;

		public BlankSceneIdentifier (string sceneName, Action<T> onCreate = null)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				throw new ArgumentException("Scene name cannot be null or empty.", nameof(sceneName));
			}
			m_SceneName = sceneName;
			m_OnCreate = onCreate;
		}

		public ISceneHandle CreateHandle ()
		{
			return new BlankSceneHandle(m_SceneName, m_OnCreate);
		}

		public override string ToString ()
		{
			return $"{m_SceneName} ({typeof(BlankSceneIdentifier<T>).Name})";
		}

		sealed class BlankSceneHandle : ISceneHandle
		{

			readonly string m_SceneName;
			readonly Action<T> m_OnCreate;

			public BlankSceneHandle (string sceneName, Action<T> onCreate)
			{
				m_SceneName = sceneName;
				m_OnCreate = onCreate;
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
				var entryPoint = entryPointGameObject.AddComponent<T>();
				m_OnCreate?.Invoke(entryPoint);

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