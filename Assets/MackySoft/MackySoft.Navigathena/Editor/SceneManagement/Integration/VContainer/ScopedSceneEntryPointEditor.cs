#if ENABLE_NAVIGATHENA_VCONTAINER
using UnityEditor;
using UnityEngine;
using VContainer.Unity;

namespace MackySoft.Navigathena.SceneManagement.VContainer
{

	[CustomEditor(typeof(ScopedSceneEntryPoint), true)]
	public sealed class ScopedSceneEntryPointEditor : Editor
	{

		static readonly GUILayoutOption[] m_ButtonLayoutOptions = new GUILayoutOption[]
		{
			GUILayout.Height(EditorGUIUtility.singleLineHeight * 2f)
		};

		ScopedSceneEntryPoint m_Target;
		LifetimeScope m_LifetimeScope;

		void OnEnable ()
		{
			m_Target = (ScopedSceneEntryPoint)target;
			m_LifetimeScope = m_Target.GetComponentInChildren<LifetimeScope>(true);
		}

		public override void OnInspectorGUI ()
		{
			base.OnInspectorGUI();

			if (m_LifetimeScope == null)
			{
				EditorGUILayout.Space();
				if (GUILayout.Button("Create Default LifetimeScope", m_ButtonLayoutOptions))
				{
					CreateLifetimeScope();
				}
			}
		}

		void CreateLifetimeScope ()
		{
			if (m_LifetimeScope != null)
			{
				return;
			}

			GameObject go = new GameObject(nameof(LifetimeScope));
			go.SetActive(false);
			go.transform.SetParent(m_Target.transform);
			go.transform.localPosition = Vector3.zero;
			go.transform.localRotation = Quaternion.identity;
			go.transform.localScale = Vector3.one;

			m_LifetimeScope = go.AddComponent<LifetimeScope>();
			m_LifetimeScope.autoRun = false;

			Selection.activeGameObject = go;
			Undo.RegisterCreatedObjectUndo(go, "Create LifetimeScope");
		}
	}
}
#endif