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

	public interface ISceneHandle
	{
		UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default);
		UniTask Unload (IProgress<float> progress = null, CancellationToken cancellationToken = default);
	}
}