using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MackySoft.Navigathena.SceneManagement.Utilities
{
	public static class SceneUtility
	{

		public static bool TryGetComponentInScene<T> (this Scene scene, out T result, bool includeInactive)
		{
			if (!scene.IsValid())
			{
				throw new ArgumentException("Scene is invalid.", nameof(scene));
			}
			foreach (GameObject rootGameObject in scene.GetRootGameObjects())
			{
				result = rootGameObject.GetComponentInChildren<T>(includeInactive);
				if (result != null)
				{
					return true;
				}
			}
			result = default;
			return false;
		}

		public static T GetComponentInScene<T> (this Scene scene, bool includeInactive)
		{
			return TryGetComponentInScene(scene, out T result, includeInactive) ? result : throw new InvalidOperationException($"Component of type '{typeof(T).Name}' is not found in scene '{scene.name}'.");
		}

		public static T GetComponentInSceneOrDefault<T> (this Scene scene, bool includeInactive)
		{
			TryGetComponentInScene(scene, out T result, includeInactive);
			return result;
		}
	}
}