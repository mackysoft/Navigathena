using NUnit.Framework;
using System.Linq;

namespace MackySoft.Navigathena.SceneManagement.Tests
{
	public static class HistoryAssert
	{
		public static void HeadsSequenceStartsWith (ISceneNavigator navigator, params ISceneIdentifier[] expected)
		{
			int i = 0;
			foreach (var entry in navigator.History)
			{
				if (i >= expected.Length)
				{
					Assert.Fail($"History count is greater than expected. Expected:{expected.Length}, Actual:{i}");
					break;
				}
				Assert.AreEqual(expected[i], entry.Scene);
				i++;
			}
		}

		public static void SequenceEqual (ISceneNavigator navigator, params ISceneIdentifier[] expected)
		{
			if (navigator.History.Count != expected.Length)
			{
				Assert.Fail($"History count is not equal to expected. Expected:{expected.Length}, Actual:{navigator.History.Count}");
				return;
			}

			int i = 0;
			foreach (var entry in navigator.History)
			{
				Assert.AreEqual(expected[i], entry.Scene);
				i++;
			}
		}

		public static void IsEmpty (ISceneNavigator navigator)
		{
			if (navigator.History.Count > 0)
			{
				Assert.Fail($"History is not empty. Count:{navigator.History.Count}");
			}
		}

		public static void Contains (ISceneNavigator navigator, ISceneIdentifier scene)
		{
			foreach (var entry in navigator.History)
			{
				if (entry.Scene == scene)
				{
					return;
				}
			}
			Assert.Fail($"History does not contain the specified scene. Scene:{scene}");
		}

		public static void DoesNotContain (ISceneNavigator navigator, ISceneIdentifier scene)
		{
			foreach (var entry in navigator.History)
			{
				if (entry.Scene == scene)
				{
					Assert.Fail($"History contains the specified scene. Scene:{scene}");
					return;
				}
			}
		}
	}
}