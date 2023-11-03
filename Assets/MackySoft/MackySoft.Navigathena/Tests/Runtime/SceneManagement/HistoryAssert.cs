using NUnit.Framework;

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
	}
}