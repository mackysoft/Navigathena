#pragma warning disable UNT0006 // Incorrect message signature
#pragma warning disable UNT0033 // Incorrect message case

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MackySoft.Navigathena
{
	public interface IAsyncOperation
	{
		UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default);
	}
}