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
			SceneEntryPointCallbackFlagsStore flags = new();
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene", x => x.Register(flags));
			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);

			Assert.AreEqual(1, m_Navigator.History.Count);
			Assert.AreEqual(firstSceneIdentifier, m_Navigator.History.First().Scene);
			Assert.AreEqual(SceneEntryPointCallbackFlags.OnInitialize | SceneEntryPointCallbackFlags.OnEnter, flags.Value);
		});

		[UnityTest]
		public IEnumerator Push_canceled_if_interrupt_transition_OnEnter () => UniTask.ToCoroutine(async () =>
		{
			var interruptSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("InterruptScene");
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene", entryPoint =>
			{
				entryPoint.SetCallbacks(
					onEnter: (reader, ct) => m_Navigator.Push(interruptSceneIdentifier)
				);
			});
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
				return;
			}

			Assert.Fail();
		});

		[UnityTest]
		public IEnumerator Push_canceled_and_OnExit_is_not_called_if_interrupt_transition_OnInitialize () => UniTask.ToCoroutine(async () =>
		{
			var interruptSceneIdentifier2 = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("InterruptScene2");
			var interruptSceneIdentifier1 = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("InterruptScene1", entryPoint =>
			{
				entryPoint.SetCallbacks(
					onInitialize: (reader, progress, ct) => m_Navigator.Push(interruptSceneIdentifier2),
					onExit: (writer, ct) => UniTask.FromException(new InvalidOperationException("OnExit is called."))
				);
			});
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene", entryPoint =>
			{
				entryPoint.SetCallbacks(
					onInitialize: (reader, progress, ct) => m_Navigator.Push(interruptSceneIdentifier1),
					onExit: (writer, ct) => UniTask.FromException(new InvalidOperationException("OnExit is called."))
				);
			});
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
				return;
			}

			Assert.Fail();
		});

		[UnityTest]
		public IEnumerator Pop_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointCallbackFlagsStore flags = new();
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");
			var secondSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("SecondScene", x => x.Register(flags));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Push(secondSceneIdentifier);

			await m_Navigator.Pop();

			Assert.AreEqual(SceneEntryPointCallbackFlags.All, flags.Value);
			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);
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
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);

			try
			{
				await m_Navigator.Pop();
				Assert.Fail();
			}
			catch (Exception e)
			{
				Assert.Pass(e.ToString());
			}
		});

		[UnityTest]
		public IEnumerator Pop_successfully_if_interrupt_pop () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointCallbackFlagsStore secondFlags = new();
			SceneEntryPointCallbackFlagsStore thirdFlags = new();
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");
			var secondSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("SecondScene", x => x.Register(secondFlags));
			var thirdSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("ThirdScene", x => x.Register(thirdFlags));

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

			Assert.AreEqual(SceneEntryPointCallbackFlags.All, secondFlags.Value);
			Assert.AreEqual(SceneEntryPointCallbackFlags.All, thirdFlags.Value);
			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);
		});

		[UnityTest]
		public IEnumerator Change_successfully () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointCallbackFlagsStore flags = new();
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");
			var secondSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("SecondScene", x => x.Register(flags));

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Change(secondSceneIdentifier);

			HistoryAssert.SequenceEqual(
				m_Navigator,
				secondSceneIdentifier
			);
		});

		[UnityTest]
		public IEnumerator Change_successfully_if_interrupt_change () => UniTask.ToCoroutine(async () =>
		{
			SceneEntryPointCallbackFlagsStore secondFlags = new();
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");
			var secondSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("SecondScene", x => x.Register(secondFlags));
			var thirdSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("ThirdScene");

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

			Assert.AreEqual(SceneEntryPointCallbackFlags.None, secondFlags.Value);
			HistoryAssert.SequenceEqual(
				m_Navigator,
				thirdSceneIdentifier
			);
		});

		[UnityTest]
		public IEnumerator Replace_successfully () => UniTask.ToCoroutine(async () =>
		{
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");
			var secondSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("SecondScene");
			var thirdSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("ThirdScene");

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
		});

		[UnityTest]
		public IEnumerator Replace_throw_if_history_is_empty () => UniTask.ToCoroutine(async () =>
		{
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene");

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
			int initializeCount = 0;
			int finalizeCount = 0;
			var firstSceneIdentifier = new BlankSceneIdentifier<AnonymousSceneEntryPoint>("FirstScene", x =>
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
			});

			await m_Navigator.Initialize();

			await m_Navigator.Push(firstSceneIdentifier);
			await m_Navigator.Reload();

			Assert.AreEqual(2, initializeCount);
			Assert.AreEqual(1, finalizeCount);
			HistoryAssert.SequenceEqual(
				m_Navigator,
				firstSceneIdentifier
			);
		});
	}
}