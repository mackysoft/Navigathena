using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MackySoft.Navigathena.SceneManagement
{

	public interface ISceneEntryPoint
	{
		UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken);
		UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken);
		UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken);
		UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken);

#if UNITY_EDITOR
		UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken);
#endif
	}

	public abstract class SceneEntryPointBase : MonoBehaviour, ISceneEntryPoint
	{

		protected virtual UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => UniTask.CompletedTask;

		protected virtual UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken) => UniTask.CompletedTask;

		protected virtual UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken) => UniTask.CompletedTask;

		protected virtual UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => UniTask.CompletedTask;

#if UNITY_EDITOR
		protected virtual UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken) => UniTask.CompletedTask;
#endif

		#region Explicit ISceneEntryPoint Implementations

		UniTask ISceneEntryPoint.OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => OnInitialize(reader, progress, cancellationToken);

		UniTask ISceneEntryPoint.OnEnter (ISceneDataReader reader, CancellationToken cancellationToken) => OnEnter(reader, cancellationToken);

		UniTask ISceneEntryPoint.OnExit (ISceneDataWriter writer, CancellationToken cancellationToken) => OnExit(writer, cancellationToken);

		UniTask ISceneEntryPoint.OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => OnFinalize(writer, progress, cancellationToken);

#if UNITY_EDITOR
		UniTask ISceneEntryPoint.OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken) => OnEditorFirstPreInitialize(writer, cancellationToken);
#endif

		#endregion

	}
}