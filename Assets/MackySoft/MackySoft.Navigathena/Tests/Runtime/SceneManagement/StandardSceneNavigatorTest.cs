using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Collections;
using System.Linq;
using UnityEngine.TestTools;

namespace MackySoft.Navigathena.SceneManagement.Tests
{

	[TestFixture]
	public class StandardSceneNavigatorTest
	{

		StandardSceneNavigator m_Navigator;

		[UnitySetUp]
		public IEnumerator SetUp () => UniTask.ToCoroutine(async () =>
		{
			m_Navigator = new StandardSceneNavigator();
			await SceneManagerTestHelper.Cleanup();
		});

		[TearDown]
		public void TearDown ()
		{
			m_Navigator.Dispose();
			m_Navigator = null;
		}

		[UnityTest]
		public IEnumerator Throw_if_already_initialized () => UniTask.ToCoroutine(async () =>
		{
			await m_Navigator.Initialize();
			try
			{
				await m_Navigator.Initialize();
			}
			catch
			{
				Assert.Pass();
				return;
			}
			Assert.Fail();
		});

		[UnityTest]
		public IEnumerator Push_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);

			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});

		[UnityTest]
		public IEnumerator Push_canceled_if_interrupt_transition_OnEnter () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var interruptSceneIdentifier = new AnonymousSceneIdentifier("InterruptScene").Register(x => recorder.With(x));
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene", entryPoint =>
			{
				entryPoint.SetCallbacks(
					onEnter: (reader, ct) => m_Navigator.Push(interruptSceneIdentifier)
				);
			}).Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			try
			{
				await m_Navigator.Push(firstSceneIdentifier);
			}
			catch (OperationCanceledException)
			{
				HistoryAssert.SequenceEqual(
					m_Navigator,
					interruptSceneIdentifier,
					firstSceneIdentifier
				);

				recorder.CreateSequenceAsserter()
					.On(firstSceneIdentifier)
					.Called(SceneEntryPointCallbackFlags.OnInitialize)
					.Called(SceneEntryPointCallbackFlags.OnEnter)
					.Called(SceneEntryPointCallbackFlags.OnExit)
					.Called(SceneEntryPointCallbackFlags.OnFinalize)
					.On(interruptSceneIdentifier)
					.Called(SceneEntryPointCallbackFlags.OnInitialize)
					.Called(SceneEntryPointCallbackFlags.OnEnter)
					.SequenceEqual();

				return;
			}

			Assert.Fail();
		});

		[UnityTest]
		public IEnumerator Push_canceled_and_OnExit_is_not_called_if_interrupt_transition_OnInitialize () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var interruptSceneIdentifier2 = new AnonymousSceneIdentifier("InterruptScene2").Register(x => recorder.With(x));
			var interruptSceneIdentifier1 = new AnonymousSceneIdentifier("InterruptScene1", entryPoint =>
			{
				entryPoint.SetCallbacks(
					onInitialize: (reader, progress, ct) => m_Navigator.Push(interruptSceneIdentifier2),
					onExit: (writer, ct) => UniTask.FromException(new InvalidOperationException("OnExit is called."))
				);
			}).Register(x => recorder.With(x));
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene", entryPoint =>
			{
				entryPoint.SetCallbacks(
					onInitialize: (reader, progress, ct) => m_Navigator.Push(interruptSceneIdentifier1),
					onExit: (writer, ct) => UniTask.FromException(new InvalidOperationException("OnExit is called."))
				);
			}).Register(x => recorder.With(x));
			await m_Navigator.Initialize();

			try
			{
				await m_Navigator.Push(firstSceneIdentifier);
			}
			catch (OperationCanceledException)
			{
				HistoryAssert.SequenceEqual(
					m_Navigator,
					interruptSceneIdentifier2,
					interruptSceneIdentifier1,
					firstSceneIdentifier
				);

				recorder.CreateSequenceAsserter()
					.On(firstSceneIdentifier)
					.Called(SceneEntryPointCallbackFlags.OnInitialize)
					.Called(SceneEntryPointCallbackFlags.OnFinalize)
					.On(interruptSceneIdentifier1)
					.Called(SceneEntryPointCallbackFlags.OnInitialize)
					.Called(SceneEntryPointCallbackFlags.OnFinalize)
					.On(interruptSceneIdentifier2)
					.Called(SceneEntryPointCallbackFlags.OnInitialize)
					.Called(SceneEntryPointCallbackFlags.OnEnter)
					.SequenceEqual();

				return;
			}

			Assert.Fail();
		});

		[UnityTest]
		public IEnumerator Pop_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));
			var secondSceneIdentifier = new AnonymousSceneIdentifier("SecondScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Push(secondSceneIdentifier);

			await m_Navigator.Pop();

			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(secondSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});

		[UnityTest]
		public IEnumerator Pop_throw_if_history_is_empty () => UniTask.ToCoroutine(async () =>
		{
			await m_Navigator.Initialize();

			try
			{
				await m_Navigator.Pop();
				Assert.Fail();
			}
			catch (Exception)
			{
				HistoryAssert.IsEmpty(m_Navigator);
			}
		});

		[UnityTest]
		public IEnumerator Pop_throw_if_history_is_single_entry () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);

			try
			{
				await m_Navigator.Pop();
				Assert.Fail();
			}
			catch (Exception)
			{
				recorder.CreateSequenceAsserter()
					.On(firstSceneIdentifier)
					.Called(SceneEntryPointCallbackFlags.OnInitialize)
					.Called(SceneEntryPointCallbackFlags.OnEnter)
					.SequenceEqual();
			}
		});

		[UnityTest]
		public IEnumerator Pop_successfully_if_interrupt_pop () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));
			var secondSceneIdentifier = new AnonymousSceneIdentifier("SecondScene").Register(x => recorder.With(x));
			var thirdSceneIdentifier = new AnonymousSceneIdentifier("ThirdScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Push(secondSceneIdentifier);
			await m_Navigator.Push(thirdSceneIdentifier);

			try
			{
				await m_Navigator.Pop(interruptOperation: AsyncOperation.Create(async (progress, ct) =>
				{
					await m_Navigator.Pop();
				}));
			}
			catch (OperationCanceledException)
			{
			}

			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(secondSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(thirdSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});

		[UnityTest]
		public IEnumerator Change_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));
			var secondSceneIdentifier = new AnonymousSceneIdentifier("SecondScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Change(secondSceneIdentifier);

			HistoryAssert.SequenceEqual(
				m_Navigator,
				secondSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(secondSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});

		[UnityTest]
		public IEnumerator Change_successfully_if_interrupt_change () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));
			var secondSceneIdentifier = new AnonymousSceneIdentifier("SecondScene").Register(x => recorder.With(x));
			var thirdSceneIdentifier = new AnonymousSceneIdentifier("ThirdScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);

			try
			{
				await m_Navigator.Change(secondSceneIdentifier, interruptOperation: AsyncOperation.Create(async (progress, ct) =>
				{
					await m_Navigator.Change(thirdSceneIdentifier);
				}));
			}
			catch (OperationCanceledException)
			{
			}

			HistoryAssert.SequenceEqual(
				m_Navigator,
				thirdSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(thirdSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});

		[UnityTest]
		public IEnumerator Replace_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene").Register(x => recorder.With(x));
			var secondSceneIdentifier = new AnonymousSceneIdentifier("SecondScene").Register(x => recorder.With(x));
			var thirdSceneIdentifier = new AnonymousSceneIdentifier("ThirdScene").Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Push(secondSceneIdentifier);
			await m_Navigator.Replace(thirdSceneIdentifier);

			HistoryAssert.DoesNotContain(m_Navigator, secondSceneIdentifier);
			HistoryAssert.SequenceEqual(
				m_Navigator,
				thirdSceneIdentifier,
				firstSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(secondSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(thirdSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});

		[UnityTest]
		public IEnumerator Replace_throw_if_history_is_empty () => UniTask.ToCoroutine(async () =>
		{
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene");

			await m_Navigator.Initialize();

			try
			{
				await m_Navigator.Replace(firstSceneIdentifier);
				Assert.Fail();
			}
			catch
			{
				HistoryAssert.IsEmpty(m_Navigator);
			}
		});

		[UnityTest]
		public IEnumerator Reload_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointLifecycleSequenceRecorder recorder = new();

			int initializeCount = 0;
			int finalizeCount = 0;
			var firstSceneIdentifier = new AnonymousSceneIdentifier("FirstScene", x =>
			{
				x.SetCallbacks(
					onInitialize: (reader, progress, ct) =>
					{
						initializeCount++;
						return UniTask.CompletedTask;
					},
					onFinalize: (writer, progress, ct) =>
					{
						finalizeCount++;
						return UniTask.CompletedTask;
					}
				);
			}).Register(x => recorder.With(x));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Reload();

			Assert.AreEqual(2, initializeCount);
			Assert.AreEqual(1, finalizeCount);
			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);

			recorder.CreateSequenceAsserter()
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.Called(SceneEntryPointCallbackFlags.OnExit)
				.Called(SceneEntryPointCallbackFlags.OnFinalize)
				.On(firstSceneIdentifier)
				.Called(SceneEntryPointCallbackFlags.OnInitialize)
				.Called(SceneEntryPointCallbackFlags.OnEnter)
				.SequenceEqual();
		});
	}
}