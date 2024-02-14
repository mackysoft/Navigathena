using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Diagnostics;
using MackySoft.Navigathena.SceneManagement.Unsafe;
using MackySoft.Navigathena.SceneManagement.Utilities;
using MackySoft.Navigathena.Transitions;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement
{

	/// <summary>
	/// <para> Default SceneNavigator used for Navigathena scene management. </para>
	/// <para> If you need to implement custom logic, use <see cref="GlobalSceneNavigator.Register(ISceneNavigator)"/>. </para>
	/// </summary>
	public sealed class StandardSceneNavigator : ISceneNavigator, IUnsafeSceneNavigator, IDisposable
	{

		enum TransitionEnterStage
		{
			EditorFirstPreInitializing = 0,
			Initializing = 1,
			Entering = 2,
		}

		/// <summary>
		/// History of all scenes, including the current scene.
		/// Since interrupt processing may occur on ISceneEntryPoint events, the history must be updated prior to calling the event.
		/// </summary>
		readonly History<SceneHistoryEntry> m_History = new();

		/// <summary>
		/// A counter to prevent <see cref="OnSceneLoaded(Scene, LoadSceneMode)"/> from causing a double initialization process.
		/// </summary>
		readonly ProcessCounter m_ProcessCounter = new();

		readonly ITransitionDirector m_DefaultTransitionDirector;
		readonly ISceneProgressFactory m_SceneProgressFactory;

		SceneState? m_CurrentSceneState;
		TransitionEnterStage? m_CurrentTransitionEnterStage;
		TransitionEnterStage? m_PreviousTransitionEnterStage;
		TransitionDirectorState? m_RunningTransitionDirectorState;
		CancellationTokenSource m_CurrentCancellationTokenSource;

		bool m_HasInitialized;
		bool m_IsDisposed;

		public IReadOnlyCollection<IReadOnlySceneHistoryEntry> History => m_History;

		public StandardSceneNavigator () : this(null, null)
		{
		}

		public StandardSceneNavigator (ITransitionDirector defaultTransitionDirector, ISceneProgressFactory sceneProgressFactory)
		{
			m_DefaultTransitionDirector = defaultTransitionDirector ?? TransitionDirector.Empty();
			m_SceneProgressFactory = sceneProgressFactory ?? new StandardSceneProgressFactory();
		}

		public async UniTask Initialize ()
		{
			ThrowIfDisposed();

			if (m_HasInitialized)
			{
				throw new SceneNavigationException($"{nameof(StandardSceneNavigator)} has already been initialized.");
			}

			m_HasInitialized = true;

			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneLoaded += OnSceneLoaded;

			// NOTE: Even multi-scene editing in the editor must be performed within Start to ensure that the scene is fully loaded.
			var (scene, entryPoint) = SceneNavigatorHelper.FindFirstEntryPointInAllScenes();

			if (entryPoint == null)
			{
				return;
			}

			await InitializeFirstEntryPoint(scene, entryPoint, CancellationToken.None);
		}

		public async UniTask Push (LoadSceneRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			ThrowIfNotInitialized();

			cancellationToken.ThrowIfCancellationRequested();

			using var _ = m_ProcessCounter.Increment();
			CancellationToken ct = CancelCurrentAndCreateLinkedToken(cancellationToken);
			NavigathenaDebug.Logger.Log($"Push {request.Scene}");

			try
			{
				if (m_History.TryPeek(out SceneHistoryEntry currentSceneEntry))
				{
					if ((m_PreviousTransitionEnterStage != null) && (m_PreviousTransitionEnterStage >= TransitionEnterStage.Entering))
					{
						await m_CurrentSceneState.Value.EntryPoint.OnExit(currentSceneEntry.DataStore.Writer, ct);
						ct.ThrowIfCancellationRequested();
					}
				}

				await EnsureStartTransitionDirector(request.TransitionDirector, ct);

				IProgressDataStore progressDataStore = m_SceneProgressFactory.CreateProgressDataStore();
				await TryFinalizeAndUnloadCurrentScene(currentSceneEntry, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				await SceneNavigatorHelper.TryExecuteInterruptOperation(request.InterruptOperation, m_RunningTransitionDirectorState.Value.Progress, ct);

				m_CurrentSceneState = await SceneNavigatorHelper.LoadSceneAndGetEntryPoint(request.Scene, m_SceneProgressFactory, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				SceneDataStore sceneDataStore = new(request.Data);
				m_History.Push(new SceneHistoryEntry(request.Scene, request.TransitionDirector ?? m_DefaultTransitionDirector, sceneDataStore));

				await EnterSceneSequence(sceneDataStore.Reader, ct);
			}
			catch (OperationCanceledException)
			{
				NavigathenaDebug.Logger.Log($"Push {request.Scene} has been canceled.");
				throw;
			}
		}

		public async UniTask Pop (PopSceneRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			ThrowIfNotInitialized();

			if (m_History.Count <= 1)
			{
				throw new SceneNavigationException("SceneHistory can not pop.");
			}

			cancellationToken.ThrowIfCancellationRequested();

			using var _ = m_ProcessCounter.Increment();
			CancellationToken ct = CancelCurrentAndCreateLinkedToken(cancellationToken);

			SceneHistoryEntry currentSceneEntry = m_History.Pop();
			SceneHistoryEntry previousScene = m_History.Peek();

			NavigathenaDebug.Logger.Log($"Pop {currentSceneEntry.Scene}");
			try
			{
				if ((m_PreviousTransitionEnterStage != null) && (m_PreviousTransitionEnterStage >= TransitionEnterStage.Entering))
				{
					await m_CurrentSceneState.Value.EntryPoint.OnExit(currentSceneEntry.DataStore.Writer, ct);
					ct.ThrowIfCancellationRequested();
				}

				await EnsureStartTransitionDirector(request.OverrideTransitionDirector ?? currentSceneEntry.TransitionDirector, ct);

				IProgressDataStore progressDataStore = m_SceneProgressFactory.CreateProgressDataStore();
				await TryFinalizeAndUnloadCurrentScene(currentSceneEntry, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				await SceneNavigatorHelper.TryExecuteInterruptOperation(request.InterruptOperation, m_RunningTransitionDirectorState.Value.Progress, ct);

				m_CurrentSceneState = await SceneNavigatorHelper.LoadSceneAndGetEntryPoint(previousScene.Scene, m_SceneProgressFactory, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);

				await EnterSceneSequence(previousScene.DataStore.Reader, ct);
			}
			catch (OperationCanceledException)
			{
				NavigathenaDebug.Logger.Log($"Pop {currentSceneEntry.Scene} has been canceled.");
				throw;
			}
		}

		public async UniTask Change (LoadSceneRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			ThrowIfNotInitialized();

			cancellationToken.ThrowIfCancellationRequested();

			using var _ = m_ProcessCounter.Increment();
			CancellationToken ct = CancelCurrentAndCreateLinkedToken(cancellationToken);
			NavigathenaDebug.Logger.Log($"Change {request.Scene}");

			try
			{
				if (m_History.TryPeek(out SceneHistoryEntry currentSceneEntry))
				{
					if ((m_PreviousTransitionEnterStage != null) && (m_PreviousTransitionEnterStage >= TransitionEnterStage.Entering))
					{
						await m_CurrentSceneState.Value.EntryPoint.OnExit(currentSceneEntry.DataStore.Writer, ct);
						ct.ThrowIfCancellationRequested();
					}
				}

				await EnsureStartTransitionDirector(request.TransitionDirector, ct);

				IProgressDataStore progressDataStore = m_SceneProgressFactory.CreateProgressDataStore();
				await TryFinalizeAndUnloadCurrentScene(currentSceneEntry, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				await SceneNavigatorHelper.TryExecuteInterruptOperation(request.InterruptOperation, m_RunningTransitionDirectorState.Value.Progress, ct);

				m_CurrentSceneState = await SceneNavigatorHelper.LoadSceneAndGetEntryPoint(request.Scene, m_SceneProgressFactory, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);

				SceneDataStore sceneDataStore = new(request.Data);
				m_History.Clear();
				m_History.Push(new SceneHistoryEntry(request.Scene, request.TransitionDirector ?? m_DefaultTransitionDirector, sceneDataStore));

				await EnterSceneSequence(sceneDataStore.Reader, ct);
			}
			catch (OperationCanceledException)
			{
				NavigathenaDebug.Logger.Log($"Change {request.Scene} has been canceled.");
				throw;
			}
		}

		public async UniTask Replace (LoadSceneRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			ThrowIfNotInitialized();

			if (m_History.Count == 0)
			{
				throw new SceneNavigationException("SceneHistory is empty.");
			}

			cancellationToken.ThrowIfCancellationRequested();

			using var _ = m_ProcessCounter.Increment();
			CancellationToken ct = CancelCurrentAndCreateLinkedToken(cancellationToken);
			NavigathenaDebug.Logger.Log($"Replace {request.Scene}");

			try
			{
				SceneHistoryEntry currentSceneEntry = m_History.Peek();

				if ((m_PreviousTransitionEnterStage != null) && (m_PreviousTransitionEnterStage >= TransitionEnterStage.Entering))
				{
					await m_CurrentSceneState.Value.EntryPoint.OnExit(currentSceneEntry.DataStore.Writer, ct);
					ct.ThrowIfCancellationRequested();
				}

				await EnsureStartTransitionDirector(request.TransitionDirector, ct);

				IProgressDataStore progressDataStore = m_SceneProgressFactory.CreateProgressDataStore();
				await TryFinalizeAndUnloadCurrentScene(currentSceneEntry, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				await SceneNavigatorHelper.TryExecuteInterruptOperation(request.InterruptOperation, m_RunningTransitionDirectorState.Value.Progress, ct);

				m_CurrentSceneState = await SceneNavigatorHelper.LoadSceneAndGetEntryPoint(request.Scene, m_SceneProgressFactory, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);

				SceneDataStore sceneDataStore = new(request.Data);
				m_History.Pop();
				m_History.Push(new SceneHistoryEntry(request.Scene, request.TransitionDirector ?? m_DefaultTransitionDirector, sceneDataStore));

				await EnterSceneSequence(sceneDataStore.Reader, ct);
			}
			catch (OperationCanceledException)
			{
				NavigathenaDebug.Logger.Log($"Replace {request.Scene} has been canceled.");
				throw;
			}
		}

		public async UniTask Reload (ReloadSceneRequest request, CancellationToken cancellationToken = default)
		{
			ThrowIfDisposed();
			ThrowIfNotInitialized();

			if (m_History.Count == 0)
			{
				throw new SceneNavigationException("SceneHistory is empty.");
			}

			cancellationToken.ThrowIfCancellationRequested();

			using var _ = m_ProcessCounter.Increment();
			CancellationToken ct = CancelCurrentAndCreateLinkedToken(cancellationToken);

			SceneHistoryEntry currentSceneEntry = m_History.Peek();
			NavigathenaDebug.Logger.Log($"Reload {currentSceneEntry.Scene}");
			try
			{
				if ((m_PreviousTransitionEnterStage != null) && (m_PreviousTransitionEnterStage >= TransitionEnterStage.Entering))
				{
					await m_CurrentSceneState.Value.EntryPoint.OnExit(currentSceneEntry.DataStore.Writer, ct);
					ct.ThrowIfCancellationRequested();
				}

				await EnsureStartTransitionDirector(request.OverrideTransitionDirector ?? currentSceneEntry.TransitionDirector, ct);

				IProgressDataStore progressDataStore = m_SceneProgressFactory.CreateProgressDataStore();
				await TryFinalizeAndUnloadCurrentScene(currentSceneEntry, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				await SceneNavigatorHelper.TryExecuteInterruptOperation(request.InterruptOperation, m_RunningTransitionDirectorState.Value.Progress, ct);

				m_CurrentSceneState = await SceneNavigatorHelper.LoadSceneAndGetEntryPoint(currentSceneEntry.Scene, m_SceneProgressFactory, progressDataStore, m_RunningTransitionDirectorState.Value.Progress, ct);
				await EnterSceneSequence(currentSceneEntry.DataStore.Reader, ct);
			}
			catch (OperationCanceledException)
			{
				NavigathenaDebug.Logger.Log($"Reload {currentSceneEntry.Scene} has been canceled.");
				throw;
			}
		}

		async UniTask EnsureStartTransitionDirector (ITransitionDirector transitionDirector, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (m_RunningTransitionDirectorState == null)
			{
				m_RunningTransitionDirectorState = SceneNavigatorHelper.CreateTransitionHandle(transitionDirector ?? m_DefaultTransitionDirector);
				await m_RunningTransitionDirectorState.Value.Handle.Start(cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		async UniTask TryFinalizeAndUnloadCurrentScene (SceneHistoryEntry currentSceneEntry, IProgressDataStore progressDataStore, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (currentSceneEntry != null && m_CurrentSceneState != null)
			{
				await m_CurrentSceneState.Value.EntryPoint.OnFinalize(currentSceneEntry.DataStore.Writer, progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();

				// NOTE: If the current scene is the only scene, create an empty scene to prevent an exception from being thrown when unloading.
				if (SceneManager.sceneCount < 2)
				{
					await NavigathenaBlankSceneIdentifier.Instance.CreateHandle().Load(cancellationToken: cancellationToken);
				}

				await m_CurrentSceneState.Value.Handle.Unload(m_SceneProgressFactory.CreateProgress(progressDataStore, progress), cancellationToken);
				m_CurrentSceneState = null;
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		async UniTask EnterSceneSequence (ISceneDataReader reader, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			m_CurrentTransitionEnterStage = TransitionEnterStage.Initializing;	
			await m_CurrentSceneState.Value.EntryPoint.OnInitialize(reader, m_RunningTransitionDirectorState.Value.Progress, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			await m_RunningTransitionDirectorState.Value.Handle.End(cancellationToken);
			m_RunningTransitionDirectorState = null;
			cancellationToken.ThrowIfCancellationRequested();

			m_CurrentTransitionEnterStage = TransitionEnterStage.Entering;
			await m_CurrentSceneState.Value.EntryPoint.OnEnter(reader, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
		}

		async UniTask InitializeFirstEntryPoint (Scene scene, ISceneEntryPoint entryPoint, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			ISceneIdentifier identifier = new BuiltInSceneIdentifier(scene.name);
			m_CurrentSceneState = new SceneState(identifier, identifier.CreateHandle(), entryPoint);
			SceneDataStore dataStore = new();
			m_History.Push(new SceneHistoryEntry(identifier, m_DefaultTransitionDirector, dataStore));

			using var _ = m_ProcessCounter.Increment();
			CancellationToken ct = CancelCurrentAndCreateLinkedToken(cancellationToken);
			NavigathenaDebug.Logger.Log($"First Initialize {identifier}");

			try
			{
#if UNITY_EDITOR
				await entryPoint.OnEditorFirstPreInitialize(dataStore.Writer, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
#endif

				m_CurrentTransitionEnterStage = TransitionEnterStage.Initializing;
				await entryPoint.OnInitialize(dataStore.Reader, Progress.Create<IProgressDataStore>(null), cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();

				m_CurrentTransitionEnterStage = TransitionEnterStage.Entering;
				await entryPoint.OnEnter(dataStore.Reader, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException)
			{
				NavigathenaDebug.Logger.Log($"First Initialize {identifier} has been canceled.");
				throw;
			}
		}

		/// <summary>
		/// When a new scene is loaded while this SceneNavigator is not initialized, this SceneNavigator is initialized if an <see cref="ISceneEntryPoint"/> exists in the scene.
		/// </summary>
		void OnSceneLoaded (Scene scene, LoadSceneMode mode)
		{
			if (m_ProcessCounter.IsProcessing)
			{
				// Ignore when the scene is loaded by the SceneNavigator itself.
				return;
			}
			if ((m_CurrentSceneState != null) || !scene.TryGetComponentInScene(out ISceneEntryPoint entryPoint, true))
			{
				return;
			}
			InitializeFirstEntryPoint(scene, entryPoint, CancellationToken.None).Forget();
		}

		/// <summary>
		/// Cancel the current CancellationTokenSource and create a new linked token source.
		/// </summary>
		CancellationToken CancelCurrentAndCreateLinkedToken (CancellationToken cancellationToken)
		{
			m_PreviousTransitionEnterStage = m_CurrentTransitionEnterStage;
			m_CurrentTransitionEnterStage = null;

			m_CurrentCancellationTokenSource?.Cancel();
			m_CurrentCancellationTokenSource?.Dispose();
			m_CurrentCancellationTokenSource = new CancellationTokenSource();
			return CancellationTokenSource.CreateLinkedTokenSource(m_CurrentCancellationTokenSource.Token, cancellationToken).Token;
		}

		ISceneHistoryBuilder IUnsafeSceneNavigator.GetHistoryBuilderUnsafe ()
		{
			ThrowIfDisposed();
			ThrowIfNotInitialized();

			if (m_ProcessCounter.IsProcessing)
			{
				throw new InvalidOperationException("Process is currently ongoing in the SceneNavigator.");
			}
			return new StandardSceneHistoryBuilder(this);
		}

		public void Dispose ()
		{
			if (m_IsDisposed)
			{
				return;
			}
			m_IsDisposed = true;

			m_CurrentCancellationTokenSource?.Cancel();
			m_CurrentCancellationTokenSource?.Dispose();
			m_CurrentCancellationTokenSource = null;

			m_CurrentSceneState = null;
			m_History.Clear();

			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void ThrowIfDisposed ()
		{
			if (m_IsDisposed)
			{
				throw new ObjectDisposedException(nameof(StandardSceneNavigator));
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void ThrowIfNotInitialized ()
		{
			if (!m_HasInitialized)
			{
				throw new SceneNavigationException($"{nameof(StandardSceneNavigator)} has not been initialized.");
			}
		}

		sealed class StandardSceneHistoryBuilder : SceneHistoryBuilderBase
		{

			readonly StandardSceneNavigator m_Owner;
			readonly int m_Version;

			public StandardSceneHistoryBuilder (StandardSceneNavigator owner) : base(owner.m_History)
			{
				m_Owner = owner;
				m_Version = owner.m_ProcessCounter.Version;
			}

			public override void Build ()
			{
				if (m_Owner.m_ProcessCounter.IsProcessing)
				{
					throw new InvalidOperationException("Process is currently ongoing in the SceneNavigator.");
				}
				if (m_Owner.m_ProcessCounter.Version != m_Version)
				{
					throw new InvalidOperationException("The SceneNavigator has been updated since the history builder was created.");
				}
				if (m_Owner.m_CurrentSceneState == null)
				{
					throw new InvalidOperationException("The current scene state is null.");
				}

				m_Owner.m_History.Clear();
				foreach (SceneHistoryEntry entry in Enumerable.Reverse(m_History))
				{
					m_Owner.m_History.Push(entry);
				}
				NavigathenaDebug.Logger.Log($"Scene history in {nameof(StandardSceneNavigator)} has been changed directly.");
			}
		}
	}
}