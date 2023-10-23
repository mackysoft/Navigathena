using System.Linq;
using UnityEditor;
using UnityEditorInternal;

namespace MackySoft.Navigathena.SceneManagement
{

	[CustomEditor(typeof(GlobalSceneNavigator))]
	public sealed class GlobalSceneNavigatorEditor : Editor
	{

		GlobalSceneNavigator m_Target;
		IReadOnlySceneHistoryEntry[] m_CachedHistory;

		ReorderableList m_HistoryList;

		void OnEnable ()
		{
			m_Target = (GlobalSceneNavigator)target;

			if (EditorApplication.isPlaying)
			{
				m_CachedHistory = m_Target.History.ToArray();

				m_HistoryList = new ReorderableList(m_CachedHistory, typeof(IReadOnlySceneHistoryEntry), false, true, false, false);
				m_HistoryList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "History");
				m_HistoryList.drawElementCallback = (rect, index, isActive, isFocused) =>
				{
					var element = m_CachedHistory[index];
					EditorGUI.LabelField(rect, element.Scene.ToString());
				};
			}
		}

		public override void OnInspectorGUI ()
		{
			if (EditorApplication.isPlaying)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					EditorGUILayout.PrefixLabel("Inner Type");
					EditorGUILayout.LabelField(m_Target.InnerType?.Name ?? "Not initialized");
				}

				EditorGUILayout.Space();

				m_HistoryList.DoLayoutList();
			}
			else
			{
				EditorGUILayout.HelpBox("The history is only available during play mode.", MessageType.Info);
			}
		}
	}
}
