using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Transitions;

namespace MackySoft.Navigathena.SceneManagement
{

	public readonly struct LoadSceneRequest
	{

		public ISceneIdentifier Scene { get; }
		public ISceneData Data { get; }
		public ITransitionDirector TransitionDirector { get; }
		public IAsyncOperation InterruptOperation { get; }

		public LoadSceneRequest (ISceneIdentifier scene, ITransitionDirector transitionDirector, ISceneData data, IAsyncOperation interruptOperation)
		{
			Scene = scene ?? throw new ArgumentNullException(nameof(scene));
			TransitionDirector = transitionDirector;
			Data = data;
			InterruptOperation = interruptOperation;
		}
	}

	public readonly struct PopSceneRequest
	{
		public ITransitionDirector OverrideTransitionDirector { get; }
		public IAsyncOperation InterruptOperation { get; }

		public PopSceneRequest (ITransitionDirector overrideTransitionDirector, IAsyncOperation interruptOperation)
		{
			OverrideTransitionDirector = overrideTransitionDirector;
			InterruptOperation = interruptOperation;
		}
	}

	public readonly struct ReloadSceneRequest
	{
		public ITransitionDirector OverrideTransitionDirector { get; }
		public IAsyncOperation InterruptOperation { get; }

		public ReloadSceneRequest (ITransitionDirector overrideTransitionDirector, IAsyncOperation interruptOperation)
		{
			OverrideTransitionDirector = overrideTransitionDirector;
			InterruptOperation = interruptOperation;
		}
	}

	public interface ISceneNavigator
	{

		IReadOnlyCollection<IReadOnlySceneHistoryEntry> History { get; }

		UniTask Initialize ();

		/// <summary>
		/// Load the specified scene and add it to the scene history.
		/// </summary>
		UniTask Push (LoadSceneRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Load the first element in the scene history and remove it from the scene history.
		/// </summary>
		UniTask Pop (PopSceneRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reset the scene history and load the specified scene.
		/// </summary>
		UniTask Change (LoadSceneRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Replace the current scene with the specified scene.
		/// </summary>
		UniTask Replace (LoadSceneRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reload the current scene.
		/// </summary>
		UniTask Reload (ReloadSceneRequest request, CancellationToken cancellationToken = default);
	}

	public static class SceneNavigatorExtensions
	{

		/// <summary>
		/// Whether navigator can go back to one previous scene by <see cref="Pop(PopSceneRequest, CancellationToken)"/>.
		/// </summary>
		public static bool CanPop (this ISceneNavigator navigator)
		{
			if (navigator == null)
			{
				throw new ArgumentNullException(nameof(navigator));
			}
			return navigator.History.Count > 1;
		}

		public static UniTask Push (this ISceneNavigator navigator, ISceneIdentifier scene, ITransitionDirector transitionDirector = null, ISceneData data = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default)
		{
			if (navigator == null)
			{
				throw new ArgumentNullException(nameof(navigator));
			}
			return navigator.Push(new LoadSceneRequest(scene, transitionDirector, data, interruptOperation), cancellationToken);
		}

		public static UniTask Pop (this ISceneNavigator navigator, ITransitionDirector transitionDirector = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default)
		{
			if (navigator == null)
			{
				throw new ArgumentNullException(nameof(navigator));
			}
			return navigator.Pop(new PopSceneRequest(transitionDirector, interruptOperation), cancellationToken);
		}

		public static UniTask Change (this ISceneNavigator navigator, ISceneIdentifier scene, ITransitionDirector transitionDirector = null, ISceneData data = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default)
		{
			if (navigator == null)
			{
				throw new ArgumentNullException(nameof(navigator));
			}
			return navigator.Change(new LoadSceneRequest(scene, transitionDirector, data, interruptOperation), cancellationToken);
		}

		public static UniTask Replace (this ISceneNavigator navigator, ISceneIdentifier scene, ITransitionDirector transitionDirector = null, ISceneData data = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default)
		{
			if (navigator == null)
			{
				throw new ArgumentNullException(nameof(navigator));
			}
			return navigator.Replace(new LoadSceneRequest(scene, transitionDirector, data, interruptOperation), cancellationToken);
		}

		public static UniTask Reload (this ISceneNavigator navigator, ITransitionDirector transitionDirector = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default)
		{
			if (navigator == null)
			{
				throw new ArgumentNullException(nameof(navigator));
			}
			return navigator.Reload(new ReloadSceneRequest(transitionDirector, interruptOperation), cancellationToken);
		}
	}
}