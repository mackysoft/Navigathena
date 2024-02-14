using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Diagnostics;
using MackySoft.Navigathena.Transitions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement.Utilities
{

	/// <summary>
	/// <para> A simple set of functions for logic used in SceneNavigator. </para>
	/// <para> This may be useful when implementing a custom SceneNavigator. </para>
	/// </summary>
	public static class SceneNavigatorHelper
	{

		public static (Scene sceneThatContainsEntryPoint, ISceneEntryPoint firstEntryPoint) FindFirstEntryPointInAllScenes ()
		{
			Scene sceneThatContainsEntryPoint = default;
			ISceneEntryPoint firstEntryPoint = null;

			int sceneCount = SceneManager.sceneCount;
			for (int i = 0; i < sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (!scene.TryGetComponentInScene(out ISceneEntryPoint entryPoint, true))
				{
					continue;
				}
				if (firstEntryPoint != null)
				{
					NavigathenaDebug.Logger.Log(LogType.Error, message: "Multiple SceneEntryPoint found.", (UnityEngine.Object)entryPoint);
					continue;
				}

				sceneThatContainsEntryPoint = scene;
				firstEntryPoint = entryPoint;
			}

			return (sceneThatContainsEntryPoint, firstEntryPoint);
		}

		public static TransitionDirectorState CreateTransitionHandle (ITransitionDirector transitionDirector)
		{
			if (transitionDirector == null)
			{
				throw new ArgumentNullException(nameof(transitionDirector));
			}

			ITransitionHandle handle = transitionDirector.CreateHandle();
			IProgress<IProgressDataStore> progress = handle.AsProgress();
			return new TransitionDirectorState(handle, progress);
		}

		public static async UniTask<SceneState> LoadSceneAndGetEntryPoint (ISceneIdentifier scene, ISceneProgressFactory sceneProgressFactory, IProgressDataStore progressDataStore, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			if (scene == null)
			{
				throw new ArgumentNullException(nameof(scene));
			}

			cancellationToken.ThrowIfCancellationRequested();

			ISceneHandle sceneHandle = scene.CreateHandle();
			Scene loadedScene = await sceneHandle.Load(sceneProgressFactory.CreateProgress(progressDataStore, progress), cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			await NavigathenaBlankSceneIdentifier.Instance.CreateHandle().Unload(cancellationToken: cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			var sceneEntryPoint = loadedScene.GetComponentInScene<ISceneEntryPoint>(true);
			return new SceneState(scene, sceneHandle, sceneEntryPoint); 
		}

		public static async UniTask TryExecuteInterruptOperation (IAsyncOperation interruptOperation, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (interruptOperation == null)
			{
				return;
			}

			await interruptOperation.ExecuteAsync(progress, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
		}
	}
}