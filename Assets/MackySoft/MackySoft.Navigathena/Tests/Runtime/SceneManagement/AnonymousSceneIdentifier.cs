using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement.Tests
{
	public sealed class AnonymousSceneIdentifier : ISceneIdentifier
	{

		readonly string m_SceneName;
		readonly Action<AnonymousSceneEntryPoint> m_OnCreate;
		readonly List<ISceneEntryPointLifecycleListener> m_Listeners = new();

		public AnonymousSceneIdentifier (string sceneName, Action<AnonymousSceneEntryPoint> onCreate = null)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				throw new ArgumentException("Scene name cannot be null or empty.", nameof(sceneName));
			}
			m_SceneName = sceneName;
			m_OnCreate = onCreate;
		}

		public AnonymousSceneIdentifier Register (ISceneEntryPointLifecycleListener listener)
		{
			m_Listeners.Add(listener);
			return this;
		}

		public AnonymousSceneIdentifier Register (Func<AnonymousSceneIdentifier, ISceneEntryPointLifecycleListener> factory)
		{
			m_Listeners.Add(factory(this));
			return this;
		}

		public ISceneHandle CreateHandle ()
		{
			return new AnonymousSceneHandle(m_SceneName, m_OnCreate, m_Listeners);
		}

		public override string ToString ()
		{
			return $"{m_SceneName} {nameof(AnonymousSceneIdentifier)}";
		}

		sealed class AnonymousSceneHandle : ISceneHandle
		{

			readonly string m_SceneName;
			readonly Action<AnonymousSceneEntryPoint> m_OnCreate;
			readonly List<ISceneEntryPointLifecycleListener> m_Listeners;

			public AnonymousSceneHandle (string sceneName, Action<AnonymousSceneEntryPoint> onCreate, List<ISceneEntryPointLifecycleListener> listeners)
			{
				m_SceneName = sceneName;
				m_OnCreate = onCreate;
				m_Listeners = listeners;
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
				var entryPoint = entryPointGameObject.AddComponent<AnonymousSceneEntryPoint>();
				for (int i = 0; i < m_Listeners.Count; i++)
				{
					entryPoint.Register(m_Listeners[i]);
				}

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