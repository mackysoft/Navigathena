#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Unity.UIToolkit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class UiToolkitLiveReloadTests
    {
        private const string LiveReloadPreference = "UnityEditor.GameView.LiveReloadOn";
        private readonly List<GameObject> screens = new();
        private string assets = null!;
        private PanelSettings settings = null!;
        private UiToolkitPresentationHost host = null!;
        private bool hadPreference;
        private bool previousPreference;

        [SetUp]
        public void SetUp ()
        {
            hadPreference = EditorPrefs.HasKey(LiveReloadPreference);
            previousPreference = EditorPrefs.GetBool(LiveReloadPreference, true);
            EditorPrefs.SetBool(LiveReloadPreference, true);
            string name = "NavigathenaLiveReload-" + Guid.NewGuid().ToString("N");
            assets = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
            WriteStyle(0.75f);
            File.WriteAllText(assets + "/Controls.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Style src=\"Controls.uss\" />"
                + "<ui:Button name=\"confirm\" class=\"reload-control\" text=\"Confirm\" /></ui:UXML>");
            // A template dependency reproduces document reconstruction after a USS
            // reimport, rather than only invalidating computed style on a plain tree.
            File.WriteAllText(assets + "/Screen.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Template name=\"controls\" src=\"Controls.uxml\" />"
                + "<ui:Instance template=\"controls\" /></ui:UXML>");
            AssetDatabase.ImportAsset(assets + "/Controls.uxml", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(assets + "/Screen.uxml", ImportAssetOptions.ForceSynchronousImport);
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            host = new UiToolkitPresentationHost(settings);
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            try
            {
                foreach (GameObject screen in screens)
                {
                    Object.Destroy(screen);
                }
                screens.Clear();
                await UniTask.NextFrame();
                host?.Dispose();
                Object.Destroy(settings);
                AssetDatabase.DeleteAsset(assets);
            }
            finally
            {
                if (hadPreference)
                {
                    EditorPrefs.SetBool(LiveReloadPreference, previousPreference);
                }
                else
                {
                    EditorPrefs.DeleteKey(LiveReloadPreference);
                }
            }
        });

        [UnityTest]
        public IEnumerator Stylesheet_reimports_preserve_screen_controls_subscriptions_and_input_period () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = Create();
            IUiToolkitView view = adapter.View;
            VisualElement content = view.ContentRoot;
            Button button = content.Q<Button>("confirm");
            int clicks = 0;
            int losses = 0;
            button.clicked += () => clicks++;
            adapter.Lost += _ => losses++;
            await host.ApplyAsync(new ViewPresentationChangeSet(new[]
            {
                new ViewPresentationChange(adapter, new ViewPresentation(true, true, 0)),
            }));
            await SettleReload();
            Assert.That(button.resolvedStyle.opacity, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(view.TryCaptureInput(out UiInputSession session), Is.True);
            button.Focus();
            await UniTask.NextFrame();
            Submit(content);
            Assert.That(clicks, Is.EqualTo(1));

            foreach (float opacity in new[] { 0.5f, 0.25f })
            {
                WriteStyle(opacity);
                await SettleReload();

                Assert.That(adapter.IsAlive, Is.True, "A stylesheet refresh must not end the screen connection.");
                Assert.That(view.ContentRoot, Is.SameAs(content));
                Assert.That(content.Q<Button>("confirm"), Is.SameAs(button));
                Assert.That(content.panel, Is.Not.Null);
                Assert.That(button.resolvedStyle.opacity, Is.EqualTo(opacity).Within(0.001f));
                Assert.That(session.IsValid, Is.True);
                Assert.That(session.Revoked.IsCancellationRequested, Is.False);
                Assert.That(losses, Is.Zero);
                int before = clicks;
                Submit(content);
                Assert.That(clicks, Is.EqualTo(before + 1), "The existing focused button and subscription must still receive native submit.");
            }

            adapter.enabled = false;
            Assert.That(adapter.IsAlive, Is.False);
            Assert.That(session.IsValid, Is.False);
            Assert.That(session.Revoked.IsCancellationRequested, Is.True);
            Assert.That(losses, Is.EqualTo(1));
            adapter.enabled = true;
            WriteStyle(0.75f);
            await SettleReload();
            Assert.That(adapter.IsAlive, Is.False);
            Assert.That(view.TryCaptureInput(out _), Is.False);
            Assert.That(losses, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Stylesheet_reimport_keeps_background_input_closed_without_disabled_styling () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter back = Create();
            UiToolkitViewAdapter front = Create();
            UiToolkitViewAdapter hidden = Create();
            Button backButton = back.View.ContentRoot.Q<Button>("confirm");
            Button frontButton = front.View.ContentRoot.Q<Button>("confirm");
            Button unavailable = new();
            unavailable.SetEnabled(false);
            back.View.ContentRoot.Add(unavailable);
            int backClicks = 0;
            int frontClicks = 0;
            int backFocus = 0;
            backButton.clicked += () => backClicks++;
            frontButton.clicked += () => frontClicks++;
            backButton.RegisterCallback<FocusInEvent>(_ => backFocus++);
            await host.ApplyAsync(new ViewPresentationChangeSet(new[]
            {
                new ViewPresentationChange(back, new ViewPresentation(true, false, 0)),
                new ViewPresentationChange(front, new ViewPresentation(true, true, 1)),
                new ViewPresentationChange(hidden, new ViewPresentation(false, false, 2)),
            }));
            await SettleReload();
            Assert.That(front.View.TryCaptureInput(out UiInputSession session), Is.True);
            frontButton.Focus();
            await UniTask.NextFrame();

            WriteStyle(0.5f);
            await SettleReload();

            Assert.That(back.IsAlive && front.IsAlive && hidden.IsAlive, Is.True);
            Assert.That(back.View.TryCaptureInput(out _), Is.False);
            Assert.That(hidden.View.TryCaptureInput(out _), Is.False);
            Assert.That(hidden.View.ContentRoot.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(session.IsValid, Is.True);
            Assert.That(backButton.canGrabFocus, Is.False);
            Assert.That(backButton.enabledSelf, Is.True);
            Assert.That(unavailable.enabledSelf, Is.False);
            backButton.Focus();
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                backButton.SendEvent(submit);
            }
            await UniTask.NextFrame();
            Submit(front.View.ContentRoot);
            Assert.That(backFocus, Is.Zero);
            Assert.That(backClicks, Is.Zero);
            Assert.That(frontClicks, Is.EqualTo(1));

            await host.ApplyAsync(new ViewPresentationChangeSet(new[]
            {
                new ViewPresentationChange(front, new ViewPresentation(true, false, 1)),
                new ViewPresentationChange(back, new ViewPresentation(true, true, 0)),
            }));
            Assert.That(session.IsValid, Is.False);
            Assert.That(back.View.TryCaptureInput(out _), Is.True);
            backButton.Focus();
            await UniTask.NextFrame();
            Submit(back.View.ContentRoot);
            Assert.That(backClicks, Is.EqualTo(1));
        });

        private UiToolkitViewAdapter Create ()
        {
            GameObject screen = new("Live reload screen");
            screens.Add(screen);
            UiToolkitViewAdapter adapter = screen.AddComponent<UiToolkitViewAdapter>();
            adapter.ContentAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assets + "/Screen.uxml");
            host.Connect(adapter);
            return adapter;
        }

        private void WriteStyle (float opacity)
        {
            string value = opacity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            File.WriteAllText(assets + "/Controls.uss", ".reload-control { opacity: " + value + "; }");
            AssetDatabase.ImportAsset(assets + "/Controls.uss", ImportAssetOptions.ForceUpdate);
        }

        // Editor live reload checks asset changes at one-second intervals in Play
        // Mode. Wait past that interval before observing the resulting connection.
        private static UniTask SettleReload () => UniTask.Delay(1500, DelayType.Realtime);

        private static void Submit (VisualElement content)
        {
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            content.panel.visualTree.SendEvent(submit);
        }
    }
}
#endif
