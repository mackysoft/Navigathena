#pragma warning disable UNT0006 // Incorrect message signature

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Diagnostics;
using MackySoft.Navigathena.SceneManagement.Unsafe;
using UnityEngine;

namespace MackySoft.Navigathena.SceneManagement
{

	/// <summary>
	/// <para> A global <see cref="ISceneNavigator"/> that wraps <see cref="ISceneNavigator"/>. </para>
	/// <para> By default, this wraps <see cref="StandardSceneNavigator"/>, but <see cref="Register(ISceneNavigator)"/> can be used to register an <see cref="ISceneNavigator"/> that implements custom logic. </para>
	/// </summary>
	public sealed class GlobalSceneNavigator : MonoBehaviour, ISceneNavigator, IUnsafeSceneNavigator
	{

		static GlobalSceneNavigator s_Instance;

		ISceneNavigator m_Inner;
		bool m_HasInitialized;

		public static GlobalSceneNavigator Instance
		{
			get
			{
				EnsureInitialize();
				return s_Instance;
			}
		}

		public Type InnerType => m_Inner?.GetType();

		public IReadOnlyCollection<IReadOnlySceneHistoryEntry> History => m_Inner?.History ?? Array.Empty<IReadOnlySceneHistoryEntry>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		public static void EnsureInitialize ()
		{
			if (s_Instance != null)
			{
				return;
			}

			s_Instance = FindAnyObjectByType<GlobalSceneNavigator>();
			if (s_Instance != null)
			{
				return;
			}

			// Create a new SceneController instance as DontDestroyOnLoad if it doesn't exist.
			// If placed in the manager scene from the starting, it will work without DontDestroyOnLoad.
			var gameObject = new GameObject(nameof(GlobalSceneNavigator));
			s_Instance = gameObject.AddComponent<GlobalSceneNavigator>();
			DontDestroyOnLoad(gameObject);
		}

		/// <summary>
		/// <para> <see cref="ISceneNavigator"/> instance for scene navigation within the GlobalSceneNavigator. </para>
		/// <para> This method should be called before any scene transitions are initiated to ensure the custom navigation logic is in place. </para>
		/// <para> Once the GlobalSceneNavigator has been initialized, subsequent calls to Register will be ignored. </para>
		/// </summary>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="ArgumentException"> Thrown when attempting to register an instance of GlobalSceneNavigator itself. </exception>
		/// <exception cref="InvalidOperationException"> Thrown when attempting to register an instance of <see cref="ISceneNavigator"/> after GlobalSceneNavigator has been initialized. </exception>
		public void Register (ISceneNavigator sceneNavigator)
		{
			if (sceneNavigator == null)
			{
				throw new ArgumentNullException(nameof(sceneNavigator));
			}
			if (sceneNavigator is GlobalSceneNavigator)
			{
				throw new ArgumentException($"Cannot register {nameof(GlobalSceneNavigator)} to {nameof(GlobalSceneNavigator)}.");
			}
			if (m_HasInitialized)
			{
				throw new InvalidOperationException($"Cannot register {nameof(ISceneNavigator)} after {nameof(GlobalSceneNavigator)} has been initialized.");
			}

			m_Inner = sceneNavigator;
		}

		async UniTaskVoid Start ()
		{
			if (s_Instance != null && s_Instance != this)
			{
				Destroy(gameObject);
				return;
			}

			// Mark with DontDestroyOnLoad even if pre-existing in the scene.
			DontDestroyOnLoad(gameObject);

			// If SceneNavigator is not registered, initialize with the default SceneNavigator.
			m_Inner ??= new StandardSceneNavigator();

			m_HasInitialized = true;

			NavigathenaDebug.Logger.Log($"Initialize {nameof(GlobalSceneNavigator)} with {m_Inner.GetType().Name}.");
			await m_Inner.Initialize();
		}

		void OnDestroy ()
		{
			if (m_Inner != null && m_Inner is IDisposable disposable)
			{
				disposable.Dispose();
			}
			m_Inner = null;

			if (s_Instance == this)
			{
				s_Instance = null;
			}
		}

		public UniTask Push (LoadSceneRequest request, CancellationToken cancellationToken = default) => m_Inner.Push(request, cancellationToken);

		public UniTask Pop (PopSceneRequest request, CancellationToken cancellationToken = default) => m_Inner.Pop(request, cancellationToken);

		public UniTask Change (LoadSceneRequest request, CancellationToken cancellationToken = default) => m_Inner.Change(request, cancellationToken);

		public UniTask Replace (LoadSceneRequest request, CancellationToken cancellationToken = default) => m_Inner.Replace(request, cancellationToken);

		public UniTask Reload (ReloadSceneRequest request, CancellationToken cancellationToken = default) => m_Inner.Reload(request, cancellationToken);

		UniTask ISceneNavigator.Initialize ()
		{
			throw new NotSupportedException($"{nameof(GlobalSceneNavigator)} cannot be initialized manually.");
		}

		ISceneHistoryBuilder IUnsafeSceneNavigator.GetHistoryBuilderUnsafe ()
		{
			return m_Inner is IUnsafeSceneNavigator unsafeNavigator ? unsafeNavigator.GetHistoryBuilderUnsafe() : throw new NotSupportedException($"{m_Inner.GetType().Name} does not support {nameof(IUnsafeSceneNavigator)}.");
		}
	}
}