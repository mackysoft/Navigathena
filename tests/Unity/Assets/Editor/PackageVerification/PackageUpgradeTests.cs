using System.IO;
using System.Linq;
using MackySoft.Navigathena.Unity.UGUI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class PackageUpgradeTests
    {
        private const string Folder = "Assets/PackageUpgradeVerification";
        private const string PrefabPath = Folder + "/Screen.prefab";
        private const string ScenePath = Folder + "/Screen.unity";

        [Test]
        public void Saved_screen_references_survive_package_updates ()
        {
            string phase = File.ReadAllText(ProjectFile("PackageVerificationPhase.txt"));
            string version = File.ReadAllText(ProjectFile("PackageVerificationVersion.txt"));
            if (phase == "create")
            {
                Assert.That(AssetDatabase.IsValidFolder(Folder), Is.False);
                CreateAssets();
                File.WriteAllText(ProjectFile("PackageFixtureVersion.txt"), version);
            }
            else
            {
                Assert.That(phase, Is.EqualTo("verify"));
                Assert.That(File.ReadAllText(ProjectFile("PackageFixtureVersion.txt")), Is.Not.EqualTo(version));
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            AssertScreen(prefab);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                GameObject instance = scene.GetRootGameObjects().Single();
                AssertScreen(instance);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(instance), Is.EqualTo(prefab));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void Packaged_input_stylesheet_is_available_to_Unity_resources ()
        {
            Assert.That(Resources.Load<StyleSheet>("NavigathenaViewPermissions"), Is.Not.Null);
        }

        private static void CreateAssets ()
        {
            AssetDatabase.CreateFolder("Assets", "PackageUpgradeVerification");
            GameObject screen = new("Packaged screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasViewAdapter), typeof(ScreenPresentation));
            try
            {
                screen.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                GameObject motion = new("Motion", typeof(Animator), typeof(AnimatorScreenAnimationDriver));
                motion.transform.SetParent(screen.transform, false);
                AnimatorScreenAnimationDriver driver = motion.GetComponent<AnimatorScreenAnimationDriver>();
                using (SerializedObject animation = new(driver))
                {
                    animation.FindProperty("target").objectReferenceValue = motion.GetComponent<Animator>();
                    animation.ApplyModifiedPropertiesWithoutUndo();
                }
                using (SerializedObject presentation = new(screen.GetComponent<ScreenPresentation>()))
                {
                    SerializedProperty views = presentation.FindProperty("views");
                    views.arraySize = 1;
                    views.GetArrayElementAtIndex(0).objectReferenceValue = screen.GetComponent<CanvasViewAdapter>();
                    presentation.FindProperty("animator").objectReferenceValue = driver;
                    presentation.ApplyModifiedPropertiesWithoutUndo();
                }
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(screen, PrefabPath);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                try
                {
                    PrefabUtility.InstantiatePrefab(prefab, scene);
                    Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
                }
                finally
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
            finally
            {
                Object.DestroyImmediate(screen);
            }
        }

        private static void AssertScreen (GameObject screen)
        {
            foreach (Transform child in screen.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject), Is.Zero);
            }
            ScreenPresentation presentation = screen.GetComponent<ScreenPresentation>();
            Assert.That(presentation, Is.Not.Null);
            Assert.DoesNotThrow(presentation.ValidateConfiguration);
            AnimatorScreenAnimationDriver driver = screen.GetComponentInChildren<AnimatorScreenAnimationDriver>();
            Assert.That(driver, Is.Not.Null);
            using SerializedObject configuration = new(presentation);
            Assert.That(configuration.FindProperty("animator").objectReferenceValue, Is.EqualTo(driver));
            using SerializedObject animation = new(driver);
            Assert.That(animation.FindProperty("target").objectReferenceValue, Is.EqualTo(driver.GetComponent<Animator>()));
        }

        private static string ProjectFile (string name) => Path.Combine(Application.dataPath, "..", name);
    }
}
