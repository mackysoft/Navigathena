using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MackySoft.Navigathena.Transitions
{

	public interface ITransitionDirector
	{
		ITransitionHandle CreateHandle ();
	}

	public interface ITransitionHandle
	{
		UniTask Start (CancellationToken cancellation = default);
		UniTask End (CancellationToken cancellation = default);
	}

	public readonly struct TransitionDirectorState
	{
		public ITransitionHandle Handle { get; }
		public IProgress<IProgressDataStore> Progress { get; }

		public TransitionDirectorState (ITransitionHandle handle, IProgress<IProgressDataStore> progress)
		{
			Handle = handle;
			Progress = progress;
		}
	}

	public static class TransitionDirector
	{

		public static ITransitionDirector Empty () => EmptyTransitionDirector.Instance;

		sealed class EmptyTransitionDirector : ITransitionDirector
		{

			public static readonly EmptyTransitionDirector Instance = new EmptyTransitionDirector();

			public ITransitionHandle CreateHandle () => EmptyTransitionHandle.Instance;

			sealed class EmptyTransitionHandle : ITransitionHandle
			{

				public static readonly EmptyTransitionHandle Instance = new EmptyTransitionHandle();

				public UniTask Start (CancellationToken cancellation = default) => UniTask.CompletedTask;

				public UniTask End (CancellationToken cancellation = default) => UniTask.CompletedTask;

			}
		}
	}
}