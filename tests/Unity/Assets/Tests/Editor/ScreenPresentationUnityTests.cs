#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.MicrosoftDI;
using MackySoft.Navigathena.Unity.UGUI;
using MackySoft.Navigathena.VContainer;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class ScreenPresentationUnityTests
    {
        private readonly List<NavigationHost> hosts = new();
        private readonly List<GameObject> objects = new();
        private string assetFolder = "";

        [SetUp]
        public void SetUp ()
        {
            string name = "NavigationPresentationTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            assetFolder = "Assets/" + name;
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            Time.timeScale = 1;
            foreach (NavigationHost host in hosts)
            {
                await host.ShutdownAsync();
            }
            hosts.Clear();
            foreach (GameObject instance in objects)
            {
                if (instance != null)
                {
                    Object.Destroy(instance);
                }
            }
            objects.Clear();
            await UniTask.NextFrame();
            AssetDatabase.DeleteAsset(assetFolder);
        });

        [UnityTest]
        public IEnumerator Configured_prefab_connects_identically_with_manual_and_both_DI_modes () => UniTask.ToCoroutine(async () =>
        {
            RuntimeTestView template = CreateScreen("Template");
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(template.gameObject, assetFolder + "/Popup.prefab");
            RuntimeTestView prefab = prefabAsset.GetComponent<RuntimeTestView>();
            for (int mode = 0; mode < 3; mode++)
            {
                RuntimeTestView? acquired = null;
                Presenter? presenter = null;
                ContainerBuilder parentBuilder = new();
                using IObjectResolver parent = parentBuilder.Build();
                NavigationHost host = CreateHost(async (creation, token) =>
                {
                    acquired = await creation.InstantiateScreenAsync(prefab!, token);
                    Assert.That(acquired.GetComponent<Canvas>().enabled, Is.False);
                    Assert.That(acquired.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.False);
                    if (mode == 0)
                    {
                        return presenter = creation.Resources.CreateOwned(() => new Presenter(acquired));
                    }
                    if (mode == 1)
                    {
                        return presenter = (Presenter)creation.CreateScope(services =>
                        {
                            services.AddSingleton(acquired);
                            services.AddScreenLifecycleHandler<Presenter>();
                        });
                    }
                    return presenter = (Presenter)creation.CreateScope(parent, builder =>
                    {
                        builder.RegisterInstance(acquired);
                        builder.RegisterScreenLifecycleHandler<Presenter>();
                    });
                });
                await host.StartAsync(new Popup());
                Assert.That(acquired!.GetComponent<Canvas>().enabled, Is.True);
                Assert.That(acquired.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
                RuntimeTestView previousView = acquired;
                Presenter previousPresenter = presenter!;
                await host.Client.ResetAsync(host.Root, Destination.For(new Popup()), new NavigationOptions { RecreateInstance = true });
                Assert.That(previousView == null, Is.True);
                Assert.That(previousPresenter.Disposals, Is.EqualTo(1));
                Assert.That(presenter, Is.Not.SameAs(previousPresenter));
                Assert.That(acquired.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
                await host.ShutdownAsync();
                Assert.That(presenter!.Disposals, Is.EqualTo(1));
                Assert.That(acquired == null, Is.True);
                Assert.That(prefab != null, Is.True);
            }
        });

        [UnityTest]
        public IEnumerator Presenter_construction_failure_removes_the_acquired_instance () => UniTask.ToCoroutine(async () =>
        {
            RuntimeTestView template = CreateScreen("Template");
            RuntimeTestView? instance = null;
            NavigationHost host = CreateHost(async (creation, token) =>
            {
                instance = await creation.InstantiateScreenAsync(template, token);
                throw new InvalidOperationException("Presenter construction failed.");
            });
            try
            {
                await host.StartAsync(new Popup());
                Assert.Fail("Construction must fail.");
            }
            catch (NavigationException exception)
            {
                Assert.That(exception.DestinationCommitted, Is.False);
            }
            Assert.That(instance == null, Is.True);
            Assert.That(template != null, Is.True);
        });

        [UnityTest]
        public IEnumerator Borrowed_screen_restores_native_gates_and_is_not_destroyed () => UniTask.ToCoroutine(async () =>
        {
            RuntimeTestView view = CreateScreen("Borrowed");
            CanvasViewAdapter adapter = view.GetComponent<CanvasViewAdapter>();
            ViewPresentation original = new(true, true, 7);
            adapter.Apply(original);
            ResourceLifetime lifetime = new();
            NavigationHost host = CreateHost(async (creation, token) =>
            {
                RuntimeTestView borrowed = await creation.BorrowScreenAsync(lifetime.Reference(view), ScreenAnimationState.Foreground, token);
                Assert.That(adapter.Presentation.OutputEnabled, Is.True);
                Assert.That(adapter.Presentation.InputEnabled, Is.False);
                return creation.Resources.CreateOwned(() => new Presenter(borrowed));
            });
            await host.StartAsync(new Popup());
            await host.ShutdownAsync();
            await lifetime.EndAsync();
            Assert.That(view != null, Is.True);
            Assert.That(adapter.Presentation, Is.EqualTo(original));
        });

        [UnityTest]
        public IEnumerator Loaded_scene_can_select_one_of_multiple_configured_screens () => UniTask.ToCoroutine(async () =>
        {
            SceneAcquisition scene = new(this);
            NavigationHost host = CreateHost(async (creation, token) =>
            {
                RuntimeTestView view = await creation.LoadScreenAsync(scene, _ => scene.Selected!, token);
                return creation.Resources.CreateOwned(() => new Presenter(view));
            });
            await host.StartAsync(new Popup());
            Assert.That(scene.Selected!.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(scene.Other!.GetComponent<Canvas>().enabled, Is.False);
            await host.ShutdownAsync();
            Assert.That(scene.Scene.isLoaded, Is.False);
            Assert.That(scene.Releases, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Reset_recreates_owned_scene_UI_without_unloading_the_external_root_scene () => UniTask.ToCoroutine(async () =>
        {
            Scene externalScene = SceneManager.GetActiveScene();
            List<SceneAcquisition> acquisitions = new();
            List<Presenter> presenters = new();
            NavigationHost host = CreateHost(async (creation, token) =>
            {
                SceneAcquisition acquisition = new(this);
                acquisitions.Add(acquisition);
                RuntimeTestView view = await creation.LoadScreenAsync(acquisition, _ => acquisition.Selected!, token);
                Presenter presenter = creation.Resources.CreateOwned(() => new Presenter(view));
                presenters.Add(presenter);
                return presenter;
            });
            await host.StartAsync(new Popup());
            await host.Client.ResetAsync(host.Root, Destination.For(new Popup()), new NavigationOptions { RecreateInstance = true });
            Assert.That(acquisitions.Count, Is.EqualTo(2));
            Assert.That(acquisitions[0].Scene.isLoaded, Is.False);
            Assert.That(acquisitions[0].Releases, Is.EqualTo(1));
            Assert.That(presenters[0].Disposals, Is.EqualTo(1));
            Assert.That(acquisitions[1].Scene.isLoaded, Is.True);
            Assert.That(acquisitions[1].Selected!.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
            Assert.That(externalScene.isLoaded, Is.True);
            await host.ShutdownAsync();
            Assert.That(acquisitions[1].Releases, Is.EqualTo(1));
            Assert.That(externalScene.isLoaded, Is.True);
        });

        [UnityTest]
        public IEnumerator Animator_reaches_distinct_stable_states_while_game_is_paused () => UniTask.ToCoroutine(async () =>
        {
            AnimatorScreenAnimationDriver driver = CreateAnimator(out CanvasGroup appearance);
            Time.timeScale = 0;
            driver.SetStateImmediately(ScreenAnimationState.BeforeEnter);
            Assert.That(appearance.alpha, Is.EqualTo(0).Within(0.01f));
            await driver.PlayAsync(new ScreenAnimation(ScreenAnimationKind.Enter, ScreenAnimationState.BeforeEnter, ScreenAnimationState.Foreground), CancellationToken.None);
            Assert.That(appearance.alpha, Is.EqualTo(1).Within(0.01f));
            await driver.PlayAsync(new ScreenAnimation(ScreenAnimationKind.Cover, ScreenAnimationState.Foreground, ScreenAnimationState.Background), CancellationToken.None);
            Assert.That(appearance.alpha, Is.EqualTo(0.4f).Within(0.01f));
            await driver.PlayAsync(new ScreenAnimation(ScreenAnimationKind.Reveal, ScreenAnimationState.Background, ScreenAnimationState.Foreground), CancellationToken.None);
            Assert.That(appearance.alpha, Is.EqualTo(1).Within(0.01f));
            await driver.PlayAsync(new ScreenAnimation(ScreenAnimationKind.Exit, ScreenAnimationState.Foreground, ScreenAnimationState.AfterExit), CancellationToken.None);
            Assert.That(appearance.alpha, Is.EqualTo(0).Within(0.01f));
        });

        [UnityTest]
        public IEnumerator Cancelled_animator_cannot_overwrite_an_immediate_recovery_state () => UniTask.ToCoroutine(async () =>
        {
            AnimatorScreenAnimationDriver driver = CreateAnimator(out CanvasGroup appearance);
            using CancellationTokenSource cancellation = new();
            Task animation = driver.PlayAsync(new ScreenAnimation(ScreenAnimationKind.Enter, ScreenAnimationState.BeforeEnter, ScreenAnimationState.Foreground), cancellation.Token).AsTask();
            cancellation.Cancel();
            try
            {
                await animation;
                Assert.Fail("Playback must be cancelled.");
            }
            catch (OperationCanceledException)
            {
            }
            driver.SetStateImmediately(ScreenAnimationState.Background);
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(appearance.alpha, Is.EqualTo(0.4f).Within(0.01f));
        });

        [UnityTest]
        public IEnumerator External_animator_interruption_fails_and_stops_playback () => UniTask.ToCoroutine(async () =>
        {
            AnimatorScreenAnimationDriver driver = CreateAnimator(out CanvasGroup appearance);
            Task animation = driver.PlayAsync(new ScreenAnimation(ScreenAnimationKind.Enter, ScreenAnimationState.BeforeEnter, ScreenAnimationState.Foreground), CancellationToken.None).AsTask();
            Animator animator = appearance.GetComponent<Animator>();
            animator.Play("Base Layer.AfterExit", 0, 0);
            animator.Update(0);
            try
            {
                await animation;
                Assert.Fail("Interrupted playback must fail.");
            }
            catch (NavigationConfigurationException)
            {
            }
            Assert.That(animator.enabled, Is.False);
        });

        [UnityTest]
        public IEnumerator Editing_hides_its_region_ui_and_back_restores_the_same_popup_without_hiding_the_world () => UniTask.ToCoroutine(async () =>
        {
            RegionDefinitionId root = new("root");
            RegionDefinitionId left = new("left");
            RegionDefinitionId right = new("right");
            RuntimeTestView template = CreateScreen("Template");
            AnimatorScreenAnimationDriver driver = CreateAnimator(out _);
            driver.transform.SetParent(template.transform, false);
            SerializedObject presentation = new(template.GetComponent<ScreenPresentation>());
            presentation.FindProperty("animator").objectReferenceValue = driver;
            presentation.ApplyModifiedPropertiesWithoutUndo();
            RuntimeTestView prefab = PrefabUtility.SaveAsPrefabAsset(template.gameObject, assetFolder + "/Panel.prefab").GetComponent<RuntimeTestView>();
            GameObject world = new("World", typeof(SpriteRenderer));
            objects.Add(world);
            List<RegionPresenter> instances = new();
            static void Configure<T> (RouteDefinitionBuilder<T> route) where T : Route
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push;
                route.LowerPresentationPolicy = typeof(T) == typeof(EditorRoute) ? LowerPresentationPolicy.HideAndRetain
                    : typeof(T) == typeof(Popup) ? LowerPresentationPolicy.BlockInput : LowerPresentationPolicy.Preserve;
            }
            NavigationDefinition definition = NavigationDefinition.Build(root, RegionCompositionMode.Layered, builder =>
            {
                builder.AddRoute<PanelRoute>(route =>
                {
                    Configure(route);
                    foreach (RegionDefinitionId region in new[] { left, right })
                    {
                        route.AddChildRegion(region, RegionCompositionMode.Layered, RegionOccupancy.Required, child =>
                        {
                            child.AddRoute<PanelRoute>(Configure);
                            child.AddRoute<Popup>(Configure);
                            child.AddRoute<EditorRoute>(Configure);
                        });
                    }
                });
                builder.AddRoute<EditorRoute>(Configure);
            });
            ScreenDefinition<T> Define<T> () where T : Route
            {
                return new ScreenDefinition<T>(async (creation, token) =>
                {
                    RuntimeTestView view = await creation.InstantiateScreenAsync(prefab, token);
                    RegionPresenter handler = creation.Resources.CreateOwned(() => new RegionPresenter(creation.RegionId, view));
                    instances.Add(handler);
                    return handler;
                }, ScreenInstancePolicy.Multiple);
            }
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
            {
                foreach (RegionDefinitionId region in new[] { root, left, right })
                {
                    builder.RegisterScreens(region, screens =>
                    {
                        screens.RegisterScreen(Define<PanelRoute>());
                        screens.RegisterScreen(Define<EditorRoute>());
                        if (region != root)
                        {
                            screens.RegisterScreen(Define<Popup>());
                        }
                    });
                }
            });
            NavigationHost host = NavigationHost.Create(catalog);
            hosts.Add(host);
            await host.StartAsync(Destination.For(new PanelRoute())
                .Child(left, Destination.For(new PanelRoute()))
                .Child(right, Destination.For(new PanelRoute())));
            RegionInstanceId leftId = host.State.Current.Regions.Values.Single(region => region.DefinitionId == left).Id;
            RegionInstanceId rightId = host.State.Current.Regions.Values.Single(region => region.DefinitionId == right).Id;
            RegionPresenter leftHud = instances.Single(screen => screen.Region == leftId);
            RegionPresenter rightHud = instances.Single(screen => screen.Region == rightId);
            RegionPresenter shell = instances.Single(screen => screen.Region == host.Root);
            await host.Client.PushAsync(leftId, Destination.For(new Popup()));
            RegionPresenter menu = instances.Last();
            await host.Client.PushAsync(leftId, Destination.For(new EditorRoute()));
            RuntimeTestView editor = instances.Last().View;
            Assert.That(leftHud.View.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(menu.View.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(rightHud.View.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
            Assert.That(shell.View.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
            Assert.That(world.activeInHierarchy && world.GetComponent<SpriteRenderer>().enabled, Is.True);

            await host.Client.BackAsync(leftId);

            Assert.That(editor == null, Is.True);
            Assert.That(menu.View != null, Is.True);
            Assert.That(menu.View!.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
            Assert.That(menu.View.GetComponentInChildren<Animator>().GetComponent<CanvasGroup>().alpha, Is.EqualTo(1).Within(0.01f));
            Assert.That(menu.Preparations, Is.EqualTo(1));
            Assert.That(menu.Activations, Is.EqualTo(2));
            Assert.That(leftHud.View.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(leftHud.View.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.False);
            Assert.That(rightHud.Activations, Is.EqualTo(1));

            await host.Client.PushAsync(host.Root, Destination.For(new EditorRoute()));
            Assert.That(shell.View.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(rightHud.View.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(menu.View.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(world.activeInHierarchy && world.GetComponent<SpriteRenderer>().enabled, Is.True);
            await host.Client.BackAsync(host.Root);
            Assert.That(menu.View.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
            Assert.That(rightHud.View.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
        });

        private sealed record PanelRoute : Route;
        private sealed record EditorRoute : Route;

        private sealed class RegionPresenter : IScreenLifecycleHandler<Route>
        {
            public RegionPresenter (RegionInstanceId region, RuntimeTestView view)
            {
                Region = region;
                View = view;
            }
            public RegionInstanceId Region
            {
                get;
            }
            public RuntimeTestView View
            {
                get;
            }
            public int Preparations
            {
                get; private set;
            }
            public int Activations
            {
                get; private set;
            }
            public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
            public ValueTask PrepareAsync (Route route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
            {
                Preparations++;
                return default;
            }
            public ValueTask ActivateAsync (Route route, ScreenActivityContext activity)
            {
                Activations++;
                return default;
            }
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync () => default;
        }

        private RuntimeTestView CreateScreen (string name)
        {
            GameObject instance = new(name, typeof(RectTransform), typeof(Canvas));
            objects.Add(instance);
            instance.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasViewAdapter view = instance.AddComponent<CanvasViewAdapter>();
            ScreenPresentation presentation = instance.AddComponent<ScreenPresentation>();
            SerializedObject serialized = new(presentation);
            SerializedProperty views = serialized.FindProperty("views");
            views.arraySize = 1;
            views.GetArrayElementAtIndex(0).objectReferenceValue = view;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return instance.AddComponent<RuntimeTestView>();
        }

        private AnimatorScreenAnimationDriver CreateAnimator (out CanvasGroup appearance)
        {
            GameObject instance = new("Motion", typeof(CanvasGroup), typeof(Animator));
            objects.Add(instance);
            appearance = instance.GetComponent<CanvasGroup>();
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(assetFolder + "/Screen.controller");
            (string Name, float From, float To)[] states =
            {
                ("PushIn", 0, 1), ("PushOut", 1, 0.4f), ("PopIn", 0.4f, 1), ("PopOut", 1, 0),
                ("BeforeEnter", 0, 0), ("Foreground", 1, 1), ("Background", 0.4f, 0.4f), ("Hidden", 0, 0), ("AfterExit", 0, 0)
            };
            foreach (var state in states)
            {
                AnimationClip clip = new()
                {
                    name = state.Name
                };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"), AnimationCurve.Linear(0, state.From, 0.1f, state.To));
                AssetDatabase.AddObjectToAsset(clip, controller);
                controller.layers[0].stateMachine.AddState(state.Name).motion = clip;
            }
            Animator animator = instance.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            AnimatorScreenAnimationDriver driver = instance.AddComponent<AnimatorScreenAnimationDriver>();
            SerializedObject serialized = new(driver);
            serialized.FindProperty("target").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return driver;
        }

        private NavigationHost CreateHost (Func<ScreenCreationContext<Popup>, CancellationToken, ValueTask<IScreenLifecycleHandler<Popup>>> create)
        {
            RegionDefinitionId root = new("root");
            NavigationDefinition definition = NavigationDefinition.Build(root, RegionCompositionMode.Layered, builder => builder.AddRoute<Popup>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
            }));
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(root, screens => screens.RegisterScreen(new ScreenDefinition<Popup>(create, ScreenInstancePolicy.Multiple))));
            NavigationHost host = NavigationHost.Create(catalog);
            hosts.Add(host);
            return host;
        }

        public sealed record Popup : Route;

        public sealed class Presenter : IScreenLifecycleHandler<Popup>, IDisposable
        {
            private readonly RuntimeTestView view;
            public Presenter (RuntimeTestView view) => this.view = view;
            public int Disposals
            {
                get; private set;
            }
            public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
            public ValueTask PrepareAsync (Popup route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
            public ValueTask ActivateAsync (Popup route, ScreenActivityContext activity) => default;
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync () => default;
            public void Dispose ()
            {
                Assert.That(view != null, Is.True);
                Disposals++;
            }
        }

        private sealed class SceneAcquisition : IResourceAcquisition<Scene>
        {
            private readonly ScreenPresentationUnityTests fixture;
            public SceneAcquisition (ScreenPresentationUnityTests fixture) => this.fixture = fixture;
            public Scene Scene
            {
                get; private set;
            }
            public RuntimeTestView? Selected
            {
                get; private set;
            }
            public RuntimeTestView? Other
            {
                get; private set;
            }
            public int Releases
            {
                get; private set;
            }
            public ValueTask<Scene> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
            {
                Scene = SceneManager.CreateScene("Screen acquisition " + Guid.NewGuid());
                Selected = fixture.CreateScreen("Selected");
                Other = fixture.CreateScreen("Other");
                SceneManager.MoveGameObjectToScene(Selected.gameObject, Scene);
                SceneManager.MoveGameObjectToScene(Other.gameObject, Scene);
                return new ValueTask<Scene>(Scene);
            }
            public async ValueTask DisposeAsync ()
            {
                Releases++;
                await SceneManager.UnloadSceneAsync(Scene);
            }
        }
    }
}
#endif
