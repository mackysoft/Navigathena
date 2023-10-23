#if ENABLE_NAVIGATHENA_VCONTAINER
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MackySoft.Navigathena.SceneManagement.VContainer
{
	public interface ISceneLifecycle
	{
		UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken);
		UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken);
		UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken);
		UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken);

#if UNITY_EDITOR
		UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken);
#endif
	}

	public abstract class SceneLifecycleBase : ISceneLifecycle, IDisposable
	{

		CancellationTokenSource m_CancellationTokenSource;
		bool m_IsDisposed;

		protected CancellationToken CancellationTokenOnDispose
		{
			get
			{
				if (m_CancellationTokenSource == null)
				{
					m_CancellationTokenSource = new CancellationTokenSource();
					if (m_IsDisposed)
					{
						m_CancellationTokenSource.Cancel();
						m_CancellationTokenSource.Dispose();
					}
				}
				return m_CancellationTokenSource.Token;
			}
		}

		protected virtual UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => UniTask.CompletedTask;
		protected virtual UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken) => UniTask.CompletedTask;
		protected virtual UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken) => UniTask.CompletedTask;
		protected virtual UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => UniTask.CompletedTask;

		UniTask ISceneLifecycle.OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => OnInitialize(reader, progress, CreateLinkedToken(cancellationToken));
		UniTask ISceneLifecycle.OnEnter (ISceneDataReader reader, CancellationToken cancellationToken) => OnEnter(reader, CreateLinkedToken(cancellationToken));
		UniTask ISceneLifecycle.OnExit (ISceneDataWriter writer, CancellationToken cancellationToken) => OnExit(writer, CreateLinkedToken(cancellationToken));
		UniTask ISceneLifecycle.OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken) => OnFinalize(writer, progress, CreateLinkedToken(cancellationToken));

#if UNITY_EDITOR
		protected virtual UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken) => UniTask.CompletedTask;

		UniTask ISceneLifecycle.OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken) => OnEditorFirstPreInitialize(writer, CreateLinkedToken(cancellationToken));
#endif

		CancellationToken CreateLinkedToken (CancellationToken cancellationToken) => CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationTokenOnDispose).Token;

		public void Dispose ()
		{
			if (!m_IsDisposed)
			{
				m_CancellationTokenSource?.Cancel();
				m_CancellationTokenSource?.Dispose();
				DisposeCore();
				m_IsDisposed = true;
			}
		}

		protected virtual void DisposeCore ()
		{
		}
	}
}
#endif