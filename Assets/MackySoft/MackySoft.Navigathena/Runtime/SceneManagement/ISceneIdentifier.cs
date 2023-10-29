using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement
{
	public interface ISceneIdentifier
	{
		ISceneHandle CreateHandle ();
	}

	public interface ISceneIdentifier<TSceneData> : ISceneIdentifier where TSceneData : ISceneData
	{
	}

	public interface ISceneHandle
	{
		UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default);
		UniTask Unload (IProgress<float> progress = null, CancellationToken cancellationToken = default);
	}

	public static class SceneIdentifierExtensions
	{
		public static ISceneIdentifier<T> AsTypedData<T> (this ISceneIdentifier identifier) where T : ISceneData
		{
			if (identifier == null)
			{
				throw new ArgumentNullException(nameof(identifier));
			}
			if (identifier is ISceneIdentifier<T> typedIdentifier)
			{
				return typedIdentifier;
			}
			return new TypedDataSceneIdentifier<T>(identifier);
		}
	}
}