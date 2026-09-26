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
        });

        [UnityTest]
        public IEnumerator Scene_root_is_resolved_after_load_and_handler_ends_before_scene_unload () => UniTask.ToCoroutine(async () =>
        {
            TestSceneAcquisition scene = new();
            TestLifecycleHandler? handler = null;
            NavigationHost host = Create(async (preparation, token) =>
            {
                UnityScene<RuntimeTestView> loaded = await preparation.Resources.LoadSceneAsync<RuntimeTestView>(scene, token);
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
                instance = await preparation.Resources.InstantiatePrefabAsync<RuntimeTestView>(asset, token);
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
                UnityScene<RuntimeTestView> loaded = await preparation.Resources.LoadSceneAsync<RuntimeTestView>(scene, token);
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
                UnityScene<RuntimeTestView> loaded = await preparation.Resources.LoadSceneAsync<RuntimeTestView>(scene, token);
                return handler = new TestLifecycleHandler(loaded.Root) { AllowDestroyedViewOnDispose = true };
            });
            await host.StartAsync(new TestRoute());
            NavigationEntryId entry = host.State.Current.GetRegion(host.Root).Entries[0];
            Object.Destroy(handler!.View);
            await UniTask.WaitUntil(() => host.State.Current.GetPresentation(entry).Materialization == PresentationMaterialization.Lost);
            Assert.That(handler.Activity!.CancellationToken.IsCancellationRequested, Is.True);
        });

        [UnityTest]
        public IEnumerator Ui_toolkit_permissions_intercept_events_without_disabled_control_styling () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = CreateDocument();
            UIDocument document = adapter.GetComponent<UIDocument>();
            UnityEngine.UIElements.Button button = new();
            document.rootVisualElement.Add(button);
            int received = 0;
            button.RegisterCallback<PointerDownEvent>(_ => received++, TrickleDown.TrickleDown);
            await UniTask.NextFrame();
            adapter.Apply(new ViewPresentation(true, true, 2));
            await UniTask.NextFrame();
            SendPointer(button);
            Assert.That(received, Is.EqualTo(1));
            adapter.Apply(new ViewPresentation(true, false, 2));
            SendPointer(button);
            Assert.That(received, Is.EqualTo(1));
            Assert.That(button.enabledInHierarchy, Is.True);
            Assert.That(document.rootVisualElement.enabledSelf, Is.True);
            adapter.Apply(new ViewPresentation(true, true, 2));
            SendPointer(button);
            Assert.That(received, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator Reenabled_document_keeps_replacement_tree_input_closed_and_reports_loss () => CheckRebuiltDocument(false);

        [UnityTest]
        public IEnumerator Replaced_visual_tree_keeps_input_closed_and_reports_loss () => CheckRebuiltDocument(true);

        private IEnumerator CheckRebuiltDocument (bool assignAsset) => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter adapter = CreateDocument();
            UIDocument document = adapter.GetComponent<UIDocument>();
            int losses = 0;
            adapter.Lost += _ => losses++;
            adapter.Apply(new ViewPresentation(true, false, 0));
            if (assignAsset)
            {
                document.visualTreeAsset = Track(ScriptableObject.CreateInstance<VisualTreeAsset>());
            }
            else
            {
                document.enabled = false;
                document.enabled = true;
            }
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            VisualElement replacement = new();
            document.rootVisualElement.Add(replacement);
            int received = 0;
            replacement.RegisterCallback<PointerDownEvent>(_ => received++);
            SendPointer(replacement);
            Assert.That(received, Is.Zero);
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
                UnityScene<RuntimeTestView> scene = await creation.Resources.LoadSceneAsync<RuntimeTestView>(acquisition, token);
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

        private NavigationHost Create (Func<ScreenCreationContext<TestRoute>, CancellationToken, ValueTask<IScreenLifecycleHandler<TestRoute>>> factory)
        {
            NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<TestRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            }));
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(Root, screens => screens.RegisterScreen(new ScreenDefinition<TestRoute>(async (creation, token) =>
            {
                IScreenLifecycleHandler<TestRoute> handler = await factory(creation, token);
                return creation.Resources.CreateOwned(() => handler);
            }))));
            NavigationHost host = NavigationHost.Create(catalog);
            hosts.Add(host);
            return host;
        }

        private UiToolkitViewAdapter CreateDocument ()
        {
            PanelSettings settings = Track(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            GameObject root = Track(new GameObject("Document"));
            UIDocument document = root.AddComponent<UIDocument>();
            document.panelSettings = settings;
            return root.AddComponent<UiToolkitViewAdapter>();
        }

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

        private sealed record TestRoute : Route;
        private sealed class TestLifecycleHandler : IScreenLifecycleHandler<TestRoute>, IAsyncDisposable
        {
            public TestLifecycleHandler (RuntimeTestView view) => View = view;
            public RuntimeTestView View
            {
                get;
            }
            public bool Initialized
            {
                get; private set;
            }
            public bool Active
            {
                get; private set;
            }
            public bool Disposed
            {
                get; private set;
            }
            public bool AllowDestroyedViewOnDispose
            {
                get; set;
            }
            public ScreenActivityContext? Activity
            {
                get; private set;
            }
            public ValueTask InitializeAsync (CancellationToken cancellationToken)
            {
                Assert.That(View != null, Is.True);
                Initialized = true;
                return default;
            }
            public ValueTask PrepareAsync (TestRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
            public ValueTask TerminateAsync () => default;
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
            public Scene Scene
            {
                get; private set;
            }
            public bool IncludeView { get; set; } = true;
            public int Releases
            {
                get; private set;
            }
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
            public async ValueTask DisposeAsync ()
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
            public Action? BeforeRelease
            {
                get; set;
            }
            public int Releases
            {
                get; private set;
            }
            public ValueTask<GameObject> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken) => new(prefab);
            public ValueTask DisposeAsync ()
            {
                BeforeRelease?.Invoke();
                Releases++;
                return default;
            }
        }
    }
}
