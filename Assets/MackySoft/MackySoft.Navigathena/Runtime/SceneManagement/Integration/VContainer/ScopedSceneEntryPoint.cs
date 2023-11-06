#if ENABLE_NAVIGATHENA_VCONTAINER
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

namespace MackySoft.Navigathena.SceneManagement.VContainer
{

	/// <summary>
	/// This class integrates the Navigathena <see cref="ISceneEntryPoint"/> with VContainer dependency injection and executes callback functions for <see cref="ISceneLifecycle"/>,
	/// which is decoupled from Unity GameObject. When leveraging this functionality, users should not override <see cref="SceneEntryPointBase"/> callback functions.
	/// Instead, they should implement the scene lifecycle in a type inheriting from either <see cref="ISceneLifecycle"/>.
	/// This type should then be used with the DI container within a <see cref="LifetimeScope"/>.
	/// </summary>
	public class ScopedSceneEntryPoint : SceneEntryPointBase
	{

		ISceneLifecycle m_Lifecycle;

		async UniTask EnsureBuildAndInject (CancellationToken cancellationToken)
		{
			if (m_Lifecycle != null)
			{
				return;
			}

			LifetimeScope lifetimeScope = GetComponentInChildren<LifetimeScope>(true);

			if (lifetimeScope == null)
			{
				throw new InvalidOperationException($"{nameof(LifetimeScope)} is not found in the children.");
			}

			// Ensure that the LifetimeScope is not active.
			// NOTE: To accommodate app launches from all scenes, the scene LifetimeScope must not be activated before the parent LifetimeScope container is built.
			// Therefore, the scene's LifetimeScope must be inactive by default. 
			if (lifetimeScope.gameObject.activeInHierarchy)
			{
				throw new InvalidOperationException($"{nameof(LifetimeScope)} is already active. Please set the {nameof(LifetimeScope)} to inactive by default.");
			}

			cancellationToken.ThrowIfCancellationRequested();

			LifetimeScope parentLifetimeScope = await EnsureParentScope(cancellationToken);
			using (LifetimeScope.EnqueueParent(parentLifetimeScope))
			{
				lifetimeScope.autoRun = false;

				// NOTE: The parent of a LifetimeScope is set on Awake.
				lifetimeScope.gameObject.SetActive(true);
				lifetimeScope.Build();
			}

			m_Lifecycle = lifetimeScope.Container.Resolve<ISceneLifecycle>();

			cancellationToken.ThrowIfCancellationRequested();
		}

		protected virtual UniTask<LifetimeScope> EnsureParentScope (CancellationToken cancellationToken)
		{
			return UniTask.FromResult<LifetimeScope>(null);
		}

		protected sealed override async UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			await EnsureBuildAndInject(cancellationToken);
			await m_Lifecycle.OnInitialize(reader, progress, cancellationToken);
		}

		protected sealed override UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken)
		{
			return m_Lifecycle.OnEnter(reader, cancellationToken);
		}

		protected sealed override UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken)
		{
			return m_Lifecycle.OnExit(writer, cancellationToken);
		}

		protected sealed override UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			return m_Lifecycle.OnFinalize(writer, progress, cancellationToken);
		}

#if UNITY_EDITOR
		protected sealed override async UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken)
		{
			await EnsureBuildAndInject(cancellationToken);
			await m_Lifecycle.OnEditorFirstPreInitialize(writer, cancellationToken);
		}
#endif

#if UNITY_EDITOR
		protected virtual void Reset ()
		{
			LifetimeScope lifetimeScope = GetComponentInChildren<LifetimeScope>(true);
			if (lifetimeScope != null)
			{
				lifetimeScope.autoRun = false;
			}
		}
#endif

	}
}
#endif