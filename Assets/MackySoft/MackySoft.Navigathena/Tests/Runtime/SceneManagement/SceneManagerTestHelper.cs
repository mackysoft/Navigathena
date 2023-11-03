using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement.Tests
{
	public static class SceneManagerTestHelper
	{

		const string kInitSceneNamePrefix = "InitTestScene";

		public static async UniTask Cleanup ()
		{
			while (SceneManager.sceneCount > 1)
			{
				for (int i = 0; i < SceneManager.sceneCount; i++)
				{
					Scene scene = SceneManager.GetSceneAt(i);
					if (!scene.name.StartsWith(kInitSceneNamePrefix))
					{
						await SceneManager.UnloadSceneAsync(scene);
						break;
					}
				}
			}
		}
	}
}