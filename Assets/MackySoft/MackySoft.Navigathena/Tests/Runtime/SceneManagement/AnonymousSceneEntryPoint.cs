using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MackySoft.Navigathena.SceneManagement.Tests
{

	[Flags]
	public enum SceneEntryPointCallbackFlags
	{
		None = 0,
		All = OnInitialize | OnEnter | OnExit | OnFinalize,
		OnInitialize = 1 << 0,
		OnEnter = 1 << 1,
		OnExit = 1 << 2,
		OnFinalize = 1 << 3,
	}

	public sealed class SceneEntryPointCallbackFlagsStore
	{
		public SceneEntryPointCallbackFlags Value { get; set; }
	}

	public sealed class AnonymousSceneEntryPoint : SceneEntryPointBase
	{

		SceneEntryPointCallbackFlagsStore m_Flags = new();

		Func<ISceneDataReader, IProgress<IProgressDataStore>, CancellationToken, UniTask> m_OnInitialize;
		Func<ISceneDataReader, CancellationToken, UniTask> m_OnEnter;
		Func<ISceneDataWriter, CancellationToken, UniTask> m_OnExit;
		Func<ISceneDataWriter, IProgress<IProgressDataStore>, CancellationToken, UniTask> m_OnFinalize;

		public void SetCallbacks (
			Func<ISceneDataReader, IProgress<IProgressDataStore>, CancellationToken, UniTask> onInitialize = null,
			Func<ISceneDataReader, CancellationToken, UniTask> onEnter = null,
			Func<ISceneDataWriter, CancellationToken, UniTask> onExit = null,
			Func<ISceneDataWriter, IProgress<IProgressDataStore>, CancellationToken, UniTask> onFinalize = null
		)
		{
			m_OnInitialize = onInitialize;
			m_OnEnter = onEnter;
			m_OnExit = onExit;
			m_OnFinalize = onFinalize;
		}

		public void RegisterFlags (SceneEntryPointCallbackFlagsStore store)
		{
			m_Flags = store;
		}

		protected override UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			m_Flags.Value |= SceneEntryPointCallbackFlags.OnInitialize;
			return m_OnInitialize?.Invoke(reader, progress, cancellationToken) ?? UniTask.CompletedTask;
		}

		protected override UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken)
		{
			m_Flags.Value |= SceneEntryPointCallbackFlags.OnEnter;
			return m_OnEnter?.Invoke(reader, cancellationToken) ?? UniTask.CompletedTask;
		}

		protected override UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken)
		{
			m_Flags.Value |= SceneEntryPointCallbackFlags.OnExit;
			return m_OnExit?.Invoke(writer, cancellationToken) ?? UniTask.CompletedTask;
		}

		protected override UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
		{
			m_Flags.Value |= SceneEntryPointCallbackFlags.OnFinalize;
			return m_OnFinalize?.Invoke(writer, progress, cancellationToken) ?? UniTask.CompletedTask;
		}
	}
}