using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Unity.UGUI;
using MackySoft.Navigathena.Unity.UIToolkit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class CommonRuntimeUnityTests
    {
        private readonly List<Object> objects = new();
        private readonly List<NavigationHost> hosts = new();
        private readonly Dictionary<PanelSettings, UiToolkitPresentationHost> toolkitHosts = new();
        private readonly Dictionary<VisualElement, VisualElement?> toolkitFocus = new();
        private static readonly RegionDefinitionId Root = new("game");

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            foreach (NavigationHost host in hosts)
            {
                await host.ShutdownAsync();
            }

            hosts.Clear();
            foreach (Object value in objects)
            {
                if (value != null)
                {
                    Object.Destroy(value);
                }
            }

            objects.Clear();
            await UniTask.NextFrame();
            foreach (UiToolkitPresentationHost toolkit in toolkitHosts.Values) { toolkit.Dispose(); }
            toolkitHosts.Clear();
            toolkitFocus.Clear();
        });

        [UnityTest]
        public IEnumerator Scene_root_is_resolved_after_load_and_handler_ends_before_scene_unload () => UniTask.ToCoroutine(async () =>
        {
            TestSceneAcquisition scene = new();
            TestLifecycleHandler? handler = null;
            NavigationHost host = Create(async (preparation, token) =>
            {
                UnityScene<RuntimeTestView> loaded = await preparation.Lifetime.AcquireSceneAsync<RuntimeTestView>(scene, token);
                Assert.That(loaded.Scene, Is.EqualTo(scene.Scene));
                handler = new TestLifecycleHandler(loaded.Root);
                return handler;
            });
            Assert.That((await host.Start(new TestRoute()).WaitAsync()).Kind, Is.EqualTo(NavigationResultKind.Committed));
            Assert.That(handler!.Initialized, Is.True);
            Assert.That(handler.Active, Is.True);
            await host.ShutdownAsync();
            Assert.That(handler.Disposed, Is.True);
            Assert.That(scene.Scene.isLoaded, Is.False);
        });

        [UnityTest]
        public IEnumerator Prefab_instance_is_destroyed_before_the_acquired_asset_is_released () => UniTask.ToCoroutine(async () =>
        {
            GameObject prefab = Track(new GameObject("Template"));
            prefab.AddComponent<RuntimeTestView>();
            TestPrefabAcquisition asset = new(prefab);
            RuntimeTestView? instance = null;
            NavigationHost host = Create(async (preparation, token) =>
            {
                instance = await preparation.Lifetime.InstantiatePrefabAsync<RuntimeTestView>(asset, token);
                asset.BeforeRelease = () => Assert.That(instance == null, Is.True);
                Assert.That(instance.gameObject, Is.Not.SameAs(prefab));
                return new TestLifecycleHandler(instance);
            });
            await host.StartAsync(new TestRoute());
            await host.ShutdownAsync();
            Assert.That(asset.Releases, Is.EqualTo(1));
            Assert.That(prefab != null, Is.True);
        });

        [UnityTest]
        public IEnumerator Missing_scene_view_releases_the_partially_acquired_scene () => UniTask.ToCoroutine(async () =>
        {
            TestSceneAcquisition scene = new()
            {
                IncludeView = false
            };
            NavigationHost host = Create(async (preparation, token) =>
            {
                UnityScene<RuntimeTestView> loaded = await preparation.Lifetime.AcquireSceneAsync<RuntimeTestView>(scene, token);
                return new TestLifecycleHandler(loaded.Root);
            });
            try
            {
                await host.StartAsync(new TestRoute());
                Assert.Fail("A missing scene view must fail navigation.");
            }
            catch (NavigationException exception)
            {
                Assert.That(exception.DestinationCommitted, Is.False);
                Assert.That(exception.InnerException, Is.Not.Null);
            }
            Assert.That(scene.Scene.isLoaded, Is.False);
            Assert.That(scene.Releases, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Native_component_loss_invalidates_the_screen_without_a_navigation_request () => UniTask.ToCoroutine(async () =>
        {
            TestSceneAcquisition scene = new();
            TestLifecycleHandler? handler = null;
            NavigationHost host = Create(async (preparation, token) =>
            {
                UnityScene<RuntimeTestView> loaded = await preparation.Lifetime.AcquireSceneAsync<RuntimeTestView>(scene, token);
                return handler = new TestLifecycleHandler(loaded.Root) { AllowDestroyedViewOnDispose = true };
            });
            await host.StartAsync(new TestRoute());
            NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries[0];
            Object.Destroy(handler!.View);
            await UniTask.WaitUntil(() => host.State.Current.GetPresentation(entry).Materialization == PresentationMaterialization.Lost);
            Assert.That(handler.Activity!.CancellationToken.IsCancellationRequested, Is.True);
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_visibility_changes_preserve_the_document_and_its_content () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = CreateDocument();
            VisualElement root = adapter.View.ContentRoot;
            Label label = new("Retained content");
            root.Add(label);
            await UniTask.NextFrame();
            Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            await ApplyToolkitAsync(adapter, new ViewPresentation(true, false, 0));
            await UniTask.NextFrame();
            Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            await ApplyToolkitAsync(adapter, new ViewPresentation(false, false, 0));
            await UniTask.NextFrame();
            Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(adapter.IsAlive, Is.True);

            await ApplyToolkitAsync(adapter, new ViewPresentation(true, false, 0));
            await UniTask.NextFrame();
            Assert.That(root.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(adapter.View.ContentRoot, Is.SameAs(root));
            Assert.That(root.Contains(label), Is.True);
            Assert.That(label.text, Is.EqualTo("Retained content"));
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_closed_input_excludes_focus_without_disabled_control_styling () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = CreateDocument();
            UnityEngine.UIElements.Button button = new();
            adapter.View.ContentRoot.Add(button);
            int received = 0;
            int clicks = 0;
            button.RegisterCallback<PointerDownEvent>(_ => received++, TrickleDown.TrickleDown);
            button.clicked += () => clicks++;
            await UniTask.NextFrame();
            await ApplyToolkitAsync(adapter, new ViewPresentation(true, true, 2));
            await UniTask.NextFrame();
            Assert.That(button.canGrabFocus, Is.True);
            SendPointer(button);
            SendSubmit(button);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
            await ApplyToolkitAsync(adapter, new ViewPresentation(true, false, 2));
            SendPointer(button);
            SendSubmit(button);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(button.canGrabFocus, Is.False);
            Assert.That(button.enabledInHierarchy, Is.True);
            Assert.That(adapter.View.ContentRoot.enabledSelf, Is.True);
            Assert.That(button.enabledSelf, Is.True);
            Assert.That(button.focusable, Is.True);
            await ApplyToolkitAsync(adapter, new ViewPresentation(true, true, 2));
            Assert.That(button.canGrabFocus, Is.True);
            SendPointer(button);
            SendSubmit(button);
            Assert.That(received, Is.EqualTo(2));
            Assert.That(clicks, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_mouse_and_touch_cannot_focus_input_closed_controls () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = CreateDocument();
            VisualElement root = adapter.View.ContentRoot;
            UnityEngine.UIElements.Button button = new();
            root.Add(button);
            await ApplyToolkitAsync(adapter, new ViewPresentation(true, true, 0));
            await UniTask.NextFrame();
            SendPointer(button);
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(root), Is.SameAs(button));
            await ApplyToolkitAsync(adapter, new ViewPresentation(true, false, 0));
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(root), Is.Null);
            int closedFocusEntries = 0;
            root.panel.visualTree.RegisterCallback<FocusInEvent>(_ => closedFocusEntries++, TrickleDown.TrickleDown);
            SendPointer(button);
            await UniTask.NextFrame();
            using PointerDownEvent touch = PointerDownEvent.GetPooled(new Touch { fingerId = 0, position = new Vector2(10, 10), phase = TouchPhase.Began });
            button.SendEvent(touch);
            await UniTask.NextFrame();
            Assert.That(button.canGrabFocus, Is.False);
            Assert.That(ObservedFocus(root), Is.Null);
            Assert.That(closedFocusEntries, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_focus_navigation_skips_visible_input_closed_documents () => UniTask.ToCoroutine(async () =>
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            UiToolkitViewAdapter lower = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter middle = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter foreground = CreateDocumentInPanel(settings);
            VisualElement lowerRoot = lower.View.ContentRoot;
            VisualElement middleRoot = middle.View.ContentRoot;
            VisualElement foregroundRoot = foreground.View.ContentRoot;
            UnityEngine.UIElements.Button lowerButton = new() { tabIndex = 2 };
            UnityEngine.UIElements.Button middleButton = new() { tabIndex = 4 };
            UnityEngine.UIElements.Button first = new() { tabIndex = 1 };
            UnityEngine.UIElements.Button second = new() { tabIndex = 3 };
            lowerRoot.Add(lowerButton);
            middleRoot.Add(middleButton);
            foregroundRoot.Add(first);
            foregroundRoot.Add(second);
            int lowerFocusEvents = 0;
            int lowerClicks = 0;
            int firstClicks = 0;
            int secondClicks = 0;
            lowerButton.RegisterCallback<FocusEvent>(_ => lowerFocusEvents++);
            middleButton.RegisterCallback<FocusEvent>(_ => lowerFocusEvents++);
            lowerButton.clicked += () => lowerClicks++;
            middleButton.clicked += () => lowerClicks++;
            first.clicked += () => firstClicks++;
            second.clicked += () => secondClicks++;
            await ApplyToolkitAsync(lower, new ViewPresentation(true, false, 0));
            await ApplyToolkitAsync(middle, new ViewPresentation(true, false, 1));
            await ApplyToolkitAsync(foreground, new ViewPresentation(true, true, 2));
            await UniTask.NextFrame();
            first.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(first));
            SendSubmit(first);

            UnityEngine.UIElements.Button[] expected = { second, first, second, first };
            for (int index = 0; index < expected.Length; index++)
            {
                VisualElement? current = ObservedFocus(foregroundRoot) as VisualElement;
                Assert.That(current, Is.Not.Null);
                MoveFocus(current!, index >= 2);
                await UniTask.NextFrame();
                Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(expected[index]));
                SendSubmit(expected[index]);
            }

            Assert.That(lowerFocusEvents, Is.Zero);
            Assert.That(lowerClicks, Is.Zero);
            Assert.That(firstClicks, Is.EqualTo(3));
            Assert.That(secondClicks, Is.EqualTo(2));
            Assert.That(lowerRoot.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(middleRoot.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(lowerButton.canGrabFocus, Is.False);
            Assert.That(middleButton.canGrabFocus, Is.False);
            Assert.That(lowerButton.enabledSelf, Is.True);
            Assert.That(middleButton.enabledSelf, Is.True);
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_initial_focus_search_does_not_enter_input_closed_documents () => UniTask.ToCoroutine(async () =>
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            UiToolkitViewAdapter lower = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter foreground = CreateDocumentInPanel(settings);
            VisualElement lowerRoot = lower.View.ContentRoot;
            VisualElement foregroundRoot = foreground.View.ContentRoot;
            UnityEngine.UIElements.Button lowerButton = new() { tabIndex = 0 };
            UnityEngine.UIElements.Button foregroundButton = new() { tabIndex = 1 };
            lowerRoot.Add(lowerButton);
            foregroundRoot.Add(foregroundButton);
            await ApplyToolkitAsync(lower, new ViewPresentation(true, false, 0));
            await ApplyToolkitAsync(foreground, new ViewPresentation(true, true, 1));
            await UniTask.NextFrame();
            int closedFocusEntries = 0;
            lowerButton.RegisterCallback<FocusInEvent>(_ => closedFocusEntries++);
            Assert.That(ObservedFocus(foregroundRoot), Is.Null);
            MoveFocus(foregroundRoot, false);
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(foregroundButton));
            Assert.That(closedFocusEntries, Is.Zero);
            Assert.That(lowerButton.canGrabFocus, Is.False);
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_directional_navigation_skips_input_closed_documents () => UniTask.ToCoroutine(async () =>
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            UiToolkitViewAdapter lower = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter foreground = CreateDocumentInPanel(settings);
            VisualElement lowerRoot = lower.View.ContentRoot;
            VisualElement foregroundRoot = foreground.View.ContentRoot;
            UnityEngine.UIElements.Button center = AddNavigationButton(foregroundRoot, new Vector2(200, 200));
            Vector2[] offsets = { new(120, 0), new(-120, 0), new(0, -120), new(0, 120) };
            Vector2[] moves = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
            UnityEngine.UIElements.Button[] destinations = new UnityEngine.UIElements.Button[offsets.Length];
            int closedClicks = 0;
            int closedFocusEntries = 0;
            int foregroundClicks = 0;
            for (int index = 0; index < offsets.Length; index++)
            {
                UnityEngine.UIElements.Button blocked = AddNavigationButton(lowerRoot, new Vector2(200, 200) + offsets[index] / 2);
                blocked.clicked += () => closedClicks++;
                blocked.RegisterCallback<FocusInEvent>(_ => closedFocusEntries++);
                destinations[index] = AddNavigationButton(foregroundRoot, new Vector2(200, 200) + offsets[index]);
                destinations[index].clicked += () => foregroundClicks++;
            }
            await ApplyToolkitAsync(lower, new ViewPresentation(true, false, 0));
            await ApplyToolkitAsync(foreground, new ViewPresentation(true, true, 1));
            await UniTask.NextFrame();
            for (int index = 0; index < moves.Length; index++)
            {
                center.Focus();
                await UniTask.NextFrame();
                Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(center));
                using NavigationMoveEvent move = NavigationMoveEvent.GetPooled(moves[index]);
                center.SendEvent(move);
                await UniTask.NextFrame();
                Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(destinations[index]), $"Direction: {moves[index]}");
                SendSubmit(destinations[index]);
            }
            Assert.That(closedFocusEntries, Is.Zero);
            Assert.That(closedClicks, Is.Zero);
            Assert.That(foregroundClicks, Is.EqualTo(4));
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_focus_wrap_does_not_blur_the_only_input_enabled_control () => UniTask.ToCoroutine(async () =>
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            UiToolkitViewAdapter lower = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter foreground = CreateDocumentInPanel(settings);
            lower.View.ContentRoot.Add(new UnityEngine.UIElements.Button());
            VisualElement foregroundRoot = foreground.View.ContentRoot;
            UnityEngine.UIElements.Button button = new();
            foregroundRoot.Add(button);
            await ApplyToolkitAsync(lower, new ViewPresentation(true, false, 0));
            await ApplyToolkitAsync(foreground, new ViewPresentation(true, true, 1));
            await UniTask.NextFrame();
            button.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(button));
            int blurs = 0;
            button.RegisterCallback<BlurEvent>(_ => blurs++);
            for (int index = 0; index < 3; index++)
            {
                MoveFocus(button, index == 2);
                await UniTask.NextFrame();
                Assert.That(ObservedFocus(foregroundRoot), Is.SameAs(button));
            }
            Assert.That(blurs, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_closed_input_rejects_dynamic_focus_and_preserves_current_settings_on_reopen () => UniTask.ToCoroutine(async () =>
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            UiToolkitViewAdapter lower = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter foreground = CreateDocumentInPanel(settings);
            VisualElement lowerRoot = lower.View.ContentRoot;
            VisualElement foregroundRoot = foreground.View.ContentRoot;
            UnityEngine.UIElements.Button lowerButton = new() { tabIndex = 2 };
            UnityEngine.UIElements.Button foregroundButton = new();
            lowerRoot.Add(lowerButton);
            foregroundRoot.Add(foregroundButton);
            await ApplyToolkitAsync(lower, new ViewPresentation(true, true, 0));
            await ApplyToolkitAsync(foreground, new ViewPresentation(true, false, 1));
            await UniTask.NextFrame();
            lowerButton.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(lowerRoot), Is.SameAs(lowerButton));

            await ApplyToolkitAsync(lower, new ViewPresentation(true, false, 0));
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(lowerRoot), Is.Null);
            int closedFocusEntries = 0;
            lowerRoot.RegisterCallback<FocusInEvent>(_ => closedFocusEntries++, TrickleDown.TrickleDown);
            Assert.That(lowerButton.canGrabFocus, Is.False);
            await ApplyToolkitAsync(foreground, new ViewPresentation(true, true, 1));
            foregroundButton.Focus();
            await UniTask.NextFrame();
            lowerButton.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(lowerRoot), Is.Not.SameAs(lowerButton));

            UnityEngine.UIElements.Button addedWhileClosed = new() { tabIndex = 7 };
            lowerRoot.Add(addedWhileClosed);
            Assert.That(addedWhileClosed.canGrabFocus, Is.False);
            addedWhileClosed.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(lowerRoot), Is.Not.SameAs(addedWhileClosed));
            lowerRoot.focusable = true;
            Assert.That(lowerRoot.canGrabFocus, Is.False);
            lowerRoot.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(lowerRoot), Is.Not.SameAs(lowerRoot));
            Assert.That(closedFocusEntries, Is.Zero);
            lowerButton.focusable = false;
            lowerButton.tabIndex = 13;
            lowerButton.SetEnabled(false);

            await ApplyToolkitAsync(foreground, new ViewPresentation(false, false, 1));
            await ApplyToolkitAsync(lower, new ViewPresentation(true, true, 0));
            await UniTask.NextFrame();
            addedWhileClosed.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(lowerRoot), Is.SameAs(addedWhileClosed));
            Assert.That(lowerButton.focusable, Is.False);
            Assert.That(lowerButton.tabIndex, Is.EqualTo(13));
            Assert.That(lowerButton.enabledSelf, Is.False);
            Assert.That(addedWhileClosed.tabIndex, Is.EqualTo(7));
            int clicks = 0;
            addedWhileClosed.clicked += () => clicks++;
            SendSubmit(addedWhileClosed);
            Assert.That(clicks, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_closed_documents_reject_focus_when_no_input_target_is_available () => UniTask.ToCoroutine(async () =>
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            UiToolkitViewAdapter first = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter second = CreateDocumentInPanel(settings);
            VisualElement firstRoot = first.View.ContentRoot;
            VisualElement secondRoot = second.View.ContentRoot;
            UnityEngine.UIElements.Button firstButton = new();
            UnityEngine.UIElements.Button secondButton = new();
            firstRoot.Add(firstButton);
            secondRoot.Add(secondButton);
            firstRoot.focusable = true;
            await ApplyToolkitAsync(first, new ViewPresentation(true, true, 0));
            await ApplyToolkitAsync(second, new ViewPresentation(true, false, 1));
            await UniTask.NextFrame();
            firstRoot.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(firstRoot), Is.SameAs(firstRoot));

            await ApplyToolkitAsync(first, new ViewPresentation(true, false, 0));
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(firstRoot), Is.Null);
            Assert.That(firstRoot.canGrabFocus, Is.False);
            Assert.That(firstButton.canGrabFocus, Is.False);
            Assert.That(secondButton.canGrabFocus, Is.False);
            firstButton.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(firstRoot), Is.Null);
            secondButton.Focus();
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(firstRoot), Is.Null);
            MoveFocus(firstRoot, false);
            await UniTask.NextFrame();
            Assert.That(ObservedFocus(firstRoot), Is.Null);
        });

        [UnityTest]
        public IEnumerator Reenabled_adapter_does_not_revive_the_ended_input_scope () => CheckEndedView(false);

        [UnityTest]
        public IEnumerator Reactivated_screen_does_not_revive_the_ended_input_scope () => CheckEndedView(true);

        private IEnumerator CheckEndedView (bool deactivateObject) => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = CreateDocument();
            UnityEngine.UIElements.Button button = new();
            adapter.View.ContentRoot.Add(button);
            int losses = 0;
            int focus = 0;
            button.RegisterCallback<FocusInEvent>(_ => focus++);
            adapter.Lost += _ => losses++;
            await ApplyToolkitAsync(adapter, new ViewPresentation(true, true, 0));
            if (deactivateObject)
            {
                adapter.gameObject.SetActive(false);
                button.Focus();
                Assert.That(button.canGrabFocus, Is.False);
                adapter.gameObject.SetActive(true);
            }
            else
            {
                adapter.enabled = false;
                button.Focus();
                Assert.That(button.canGrabFocus, Is.False);
                adapter.enabled = true;
            }
            Assert.That(adapter.IsAlive, Is.False);
            button.Focus();
            await UniTask.NextFrame();
            Assert.That(button.canGrabFocus, Is.False);
            Assert.That(focus, Is.Zero);
            Assert.That(losses, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Canvas_input_closure_preserves_visual_interactivity_and_clears_selection () => UniTask.ToCoroutine(async () =>
        {
            GameObject root = Track(new GameObject("Canvas", typeof(Canvas)));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasViewAdapter adapter = root.AddComponent<CanvasViewAdapter>();
            GameObject buttonObject = Track(new GameObject("Button", typeof(RectTransform), typeof(UnityEngine.UI.Button)));
            buttonObject.transform.SetParent(root.transform, false);
            UnityEngine.UI.Button button = buttonObject.GetComponent<UnityEngine.UI.Button>();
            GameObject eventObject = Track(new GameObject("Event system", typeof(EventSystem)));
            EventSystem events = eventObject.GetComponent<EventSystem>();
            adapter.Apply(new ViewPresentation(true, true, 12));
            events.SetSelectedGameObject(buttonObject);
            adapter.Apply(new ViewPresentation(true, false, 12));
            Assert.That(canvas.enabled, Is.True);
            Assert.That(canvas.sortingOrder, Is.EqualTo(12));
            Assert.That(button.interactable, Is.True);
            Assert.That(root.GetComponent<GraphicRaycaster>().enabled, Is.False);
            Assert.That(events.currentSelectedGameObject, Is.Null);
            await UniTask.NextFrame();
        });

        [UnityTest]
        public IEnumerator Reload_reacquires_a_scene_on_the_Unity_context_after_the_old_scene_has_unloaded () => UniTask.ToCoroutine(async () =>
        {
            int mainThread = Environment.CurrentManagedThreadId;
            List<TestSceneAcquisition> scenes = new();
            List<TestLifecycleHandler> handlers = new();
            NavigationHost host = Create(async (creation, token) =>
            {
                await Task.Delay(1, token);
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(mainThread));
                if (scenes.Count > 0)
                {
                    Assert.That(scenes[scenes.Count - 1].Scene.isLoaded, Is.False);
                }
                TestSceneAcquisition acquisition = new();
                scenes.Add(acquisition);
                UnityScene<RuntimeTestView> scene = await creation.Lifetime.AcquireSceneAsync<RuntimeTestView>(acquisition, token);
                TestLifecycleHandler handler = new(scene.Root);
                handlers.Add(handler);
                return handler;
            });
            await host.StartAsync(new TestRoute());
            NavigationEntryId entry = handlers[0].Activity!.EntryId;
            await handlers[0].Activity!.Navigation.ReloadAsync();
            Assert.That(handlers.Count, Is.EqualTo(2));
            Assert.That(handlers[0].Disposed, Is.True);
            Assert.That(handlers[0].View == null, Is.True);
            Assert.That(handlers[1].Activity!.EntryId, Is.EqualTo(entry));
            Assert.That(handlers[1].Activity!.Reason, Is.EqualTo(ScreenActivationReason.Reload));
            Assert.That(handlers[1].Active, Is.True);
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(mainThread));
            await host.ShutdownAsync();
            Assert.That(scenes[1].Scene.isLoaded, Is.False);
        });

        [Test]
        public void Canvas_views_share_ordering_only_when_their_native_camera_matches ()
        {
            GameObject firstObject = Track(new GameObject("First canvas", typeof(Canvas)));
            GameObject secondObject = Track(new GameObject("Second canvas", typeof(Canvas)));
            Camera firstCamera = Track(new GameObject("First camera")).AddComponent<Camera>();
            Camera secondCamera = Track(new GameObject("Second camera")).AddComponent<Camera>();
            Canvas firstCanvas = firstObject.GetComponent<Canvas>();
            Canvas secondCanvas = secondObject.GetComponent<Canvas>();
            firstCanvas.renderMode = secondCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            firstCanvas.worldCamera = secondCanvas.worldCamera = firstCamera;
            CanvasViewAdapter first = firstObject.AddComponent<CanvasViewAdapter>();
            CanvasViewAdapter second = secondObject.AddComponent<CanvasViewAdapter>();

            object identity = first.Identity;
            firstObject.name = "Renamed canvas";
            Assert.That(first.Identity, Is.EqualTo(identity));
            Assert.That(first.Identity, Is.Not.EqualTo(second.Identity));
            Assert.That(first.OrderingDomain, Is.EqualTo(second.OrderingDomain));
            secondCanvas.worldCamera = secondCamera;
            Assert.That(first.OrderingDomain, Is.Not.EqualTo(second.OrderingDomain));
        }

        [Test]
        public void Toolkit_views_share_the_host_ordering_domain_but_not_native_panels ()
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            UiToolkitViewAdapter first = CreateDocumentInPanel(settings);
            UiToolkitViewAdapter second = CreateDocumentInPanel(settings);
            Assert.That(first.Identity, Is.Not.EqualTo(second.Identity));
            Assert.That(first.OrderingDomain, Is.SameAs(second.OrderingDomain));
            Assert.That(first.View.ContentRoot.panel, Is.Not.SameAs(second.View.ContentRoot.panel));
            Assert.That(settings.sortingOrder, Is.Zero);
        }

        private NavigationHost Create (Func<ScreenCreationContext<TestRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TestRoute>>> factory)
        {
            NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<TestRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.HideAndRetain));
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, screens => screens.RegisterScreen(new ScreenDefinition<TestRoute>(async (creation, token) =>
            {
                IScreenLifecycleHandler<TestRoute> handler = await factory(creation, token);
                return creation.Lifetime.CreateOwned(() => handler);
            }))));
            NavigationHost host = NavigationHost.Create(catalog);
            hosts.Add(host);
            return host;
        }

        private UiToolkitViewAdapter CreateDocument ()
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            return CreateDocumentInPanel(settings);
        }

        private UiToolkitViewAdapter CreateDocumentInPanel (PanelSettings settings)
        {
            GameObject root = Track(new GameObject("Document"));
            UiToolkitViewAdapter adapter = root.AddComponent<UiToolkitViewAdapter>();
            if (!toolkitHosts.TryGetValue(settings, out UiToolkitPresentationHost? host))
            {
                host = new UiToolkitPresentationHost(settings);
                toolkitHosts.Add(settings, host);
            }
            IUiToolkitView view = host.Connect(adapter);
            toolkitFocus.Add(view.ContentRoot, null);
            view.ContentRoot.RegisterCallback<FocusEvent>(evt => toolkitFocus[view.ContentRoot] = evt.target as VisualElement, TrickleDown.TrickleDown);
            view.ContentRoot.RegisterCallback<BlurEvent>(_ => toolkitFocus[view.ContentRoot] = null, TrickleDown.TrickleDown);
            return adapter;
        }

        private static ValueTask ApplyToolkitAsync (UiToolkitViewAdapter adapter, ViewPresentation presentation)
            => ((IViewPresentationBatchAdapter)adapter).PresentationBatch.ApplyAsync(new ViewPresentationChangeSet(
                new[] { new ViewPresentationChange(adapter, presentation) }));

        private VisualElement? ObservedFocus (VisualElement root) => toolkitFocus[root];

        private T Track<T> (T value) where T : Object
        {
            objects.Add(value);
            return value;
        }

        private static void SendPointer (VisualElement target)
        {
            using PointerDownEvent pointer = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = new Vector2(10, 10), button = 0 });
            target.SendEvent(pointer);
        }

        private static void MoveFocus (VisualElement target, bool previous)
        {
#if UNITY_2022_2_OR_NEWER
            using NavigationMoveEvent move = NavigationMoveEvent.GetPooled(previous ? NavigationMoveEvent.Direction.Previous : NavigationMoveEvent.Direction.Next);
#else
            using KeyDownEvent move = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab, character = '\t', modifiers = previous ? EventModifiers.Shift : EventModifiers.None });
#endif
            target.SendEvent(move);
        }

        private static UnityEngine.UIElements.Button AddNavigationButton (VisualElement root, Vector2 position)
        {
            UnityEngine.UIElements.Button button = new();
            button.style.position = Position.Absolute;
            button.style.left = position.x;
            button.style.top = position.y;
            button.style.width = 40;
            button.style.height = 40;
            root.Add(button);
            return button;
        }

        private static void SendSubmit (VisualElement target)
        {
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            target.SendEvent(submit);
        }

        private sealed record TestRoute : Route;
        private sealed class TestLifecycleHandler : IScreenLifecycleHandler<TestRoute>, IAsyncDisposable
        {
            public TestLifecycleHandler (RuntimeTestView view) => View = view;
            public RuntimeTestView View { get; }
            public bool Initialized { get; private set; }
            public bool Active { get; private set; }
            public bool Disposed { get; private set; }
            public bool AllowDestroyedViewOnDispose { get; set; }
            public ScreenActivityContext? Activity { get; private set; }
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
            {
                Assert.That(View != null, Is.True);
                Initialized = true;
                return default;
            }
            public ValueTask PrepareAsync (TestRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
            public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;
            public ValueTask ActivateAsync (TestRoute route, ScreenActivityContext activity)
            {
                Activity = activity;
                Active = true;
                return default;
            }
            public ValueTask DeactivateAsync ()
            {
                Active = false;
                return default;
            }
            public ValueTask DisposeAsync ()
            {
                if (!AllowDestroyedViewOnDispose)
                {
                    Assert.That(View != null, Is.True);
                }

                Disposed = true;
                return default;
            }
        }

        private sealed class TestSceneAcquisition : IResourceAcquisition<Scene>
        {
            public Scene Scene { get; private set; }
            public bool IncludeView { get; set; } = true;
            public int Releases { get; private set; }
            public ValueTask<Scene> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
            {
                Scene = SceneManager.CreateScene("Navigation test " + Guid.NewGuid());
                if (IncludeView)
                {
                    GameObject root = new("Scene view", typeof(RuntimeTestView));
                    SceneManager.MoveGameObjectToScene(root, Scene);
                }
                return new ValueTask<Scene>(Scene);
            }
            public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
            {
                Releases++;
                if (Scene.isLoaded)
                {
                    AsyncOperation operation = SceneManager.UnloadSceneAsync(Scene);
                    while (!operation.isDone)
                    {
                        await UniTask.NextFrame();
                    }
                }
            }
        }

        private sealed class TestPrefabAcquisition : IResourceAcquisition<GameObject>
        {
            private readonly GameObject prefab;
            public TestPrefabAcquisition (GameObject prefab) => this.prefab = prefab;
            public Action? BeforeRelease { get; set; }
            public int Releases { get; private set; }
            public ValueTask<GameObject> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken) => new(prefab);
            public ValueTask ReleaseAsync (NavigationProgressReporter progress)
            {
                BeforeRelease?.Invoke();
                Releases++;
                return default;
            }
        }
    }
}
