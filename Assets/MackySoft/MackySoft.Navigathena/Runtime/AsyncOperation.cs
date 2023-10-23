using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MackySoft.Navigathena
{
	public static class AsyncOperation
	{

		public static IAsyncOperation Empty ()
		{
			return EmptyAsyncOperation.Instance;
		}

		public static IAsyncOperation Create (Func<IProgress<IProgressDataStore>, CancellationToken, UniTask> executeAsync)
		{
			if (executeAsync == null)
			{
				throw new ArgumentNullException(nameof(executeAsync));
			}
			return new AnonymousAsyncOperation(executeAsync);
		}

		public static IAsyncOperation Combine (IAsyncOperation operation1, IAsyncOperation operation2)
		{
			if (operation1 == null)
			{
				throw new ArgumentNullException(nameof(operation1));
			}
			if (operation2 == null)
			{
				throw new ArgumentNullException(nameof(operation2));
			}
			return new BinaryAsyncOperation(operation1, operation2);
		}

		public static IAsyncOperation Combine (IAsyncOperation operation1, IAsyncOperation operation2, IAsyncOperation operation3)
		{
			if (operation1 == null)
			{
				throw new ArgumentNullException(nameof(operation1));
			}
			if (operation2 == null)
			{
				throw new ArgumentNullException(nameof(operation2));
			}
			if (operation3 == null)
			{
				throw new ArgumentNullException(nameof(operation3));
			}
			return new TrinaryAsyncOperation(operation1, operation2, operation3);
		}

		public static IAsyncOperation Combine (IAsyncOperation operation1, IAsyncOperation operation2, IAsyncOperation operation3, IAsyncOperation operation4)
		{
			if (operation1 == null)
			{
				throw new ArgumentNullException(nameof(operation1));
			}
			if (operation2 == null)
			{
				throw new ArgumentNullException(nameof(operation2));
			}
			if (operation3 == null)
			{
				throw new ArgumentNullException(nameof(operation3));
			}
			if (operation4 == null)
			{
				throw new ArgumentNullException(nameof(operation4));
			}
			return new QuaternaryAsyncOperation(operation1, operation2, operation3, operation4);
		}

		public static IAsyncOperation Combine (params IAsyncOperation[] operations)
		{
			if (operations == null)
			{
				throw new ArgumentNullException(nameof(operations));
			}
			IAsyncOperation[] copy = new IAsyncOperation[operations.Length];
			Array.Copy(operations, 0, copy, 0, operations.Length);
			return new NAryAsyncOperation(copy);
		}

		public static IAsyncOperation Combine (IEnumerable<IAsyncOperation> operations)
		{
			if (operations == null)
			{
				throw new ArgumentNullException(nameof(operations));
			}
			return new NAryAsyncOperation(operations.ToArray());
		}

		sealed class EmptyAsyncOperation : IAsyncOperation
		{

			public static readonly EmptyAsyncOperation Instance = new EmptyAsyncOperation();

			public EmptyAsyncOperation () { }

			public UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default) {
				cancellationToken.ThrowIfCancellationRequested();
				return UniTask.CompletedTask;
			}
		}

		sealed class AnonymousAsyncOperation : IAsyncOperation
		{

			readonly Func<IProgress<IProgressDataStore>, CancellationToken, UniTask> m_ExecuteAsync;

			public AnonymousAsyncOperation (Func<IProgress<IProgressDataStore>, CancellationToken, UniTask> executeAsync)
			{
				m_ExecuteAsync = executeAsync;
			}

			public async UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await m_ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		sealed class BinaryAsyncOperation : IAsyncOperation
		{

			readonly IAsyncOperation m_Operation1;
			readonly IAsyncOperation m_Operation2;

			public BinaryAsyncOperation (IAsyncOperation operation1, IAsyncOperation operation2)
			{
				m_Operation1 = operation1;
				m_Operation2 = operation2;
			}

			public async UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation1.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation2.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		sealed class TrinaryAsyncOperation : IAsyncOperation
		{

			readonly IAsyncOperation m_Operation1;
			readonly IAsyncOperation m_Operation2;
			readonly IAsyncOperation m_Operation3;

			public TrinaryAsyncOperation (IAsyncOperation operation1, IAsyncOperation operation2, IAsyncOperation operation3)
			{
				m_Operation1 = operation1;
				m_Operation2 = operation2;
				m_Operation3 = operation3;
			}

			public async UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation1.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation2.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation3.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		sealed class QuaternaryAsyncOperation : IAsyncOperation
		{

			readonly IAsyncOperation m_Operation1;
			readonly IAsyncOperation m_Operation2;
			readonly IAsyncOperation m_Operation3;
			readonly IAsyncOperation m_Operation4;

			public QuaternaryAsyncOperation (IAsyncOperation operation1, IAsyncOperation operation2, IAsyncOperation operation3, IAsyncOperation operation4)
			{
				m_Operation1 = operation1;
				m_Operation2 = operation2;
				m_Operation3 = operation3;
				m_Operation4 = operation4;
			}

			public async UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation1.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation2.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation3.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				await m_Operation4.ExecuteAsync(progress, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		sealed class NAryAsyncOperation : IAsyncOperation
		{

			readonly IAsyncOperation[] m_Operations;

			public NAryAsyncOperation (IAsyncOperation[] operations)
			{
				m_Operations = operations;
			}

			public async UniTask ExecuteAsync (IProgress<IProgressDataStore> progress = null, CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				foreach (var operation in m_Operations)
				{
					await operation.ExecuteAsync(progress, cancellationToken);
					cancellationToken.ThrowIfCancellationRequested();
				}
			}
		}
	}
}