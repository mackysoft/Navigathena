#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Unity.UGUI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class SceneAcquisitionUnityTests : IPrebuildSetup, IPostBuildCleanup
    {
        private const string AssetsKey = "Navigathena.SceneAcquisitionTestAssets";
        private readonly List<NavigationHost> hosts = new();
        private string folder = string.Empty;

        public void Setup ()
        {
            folder = "Assets/SceneAcquisitionTests-" + Guid.NewGuid().ToString("N");
            SessionState.SetString(AssetsKey, folder);
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            string withView = CreateScene(true);
            string withoutView = CreateScene(false);
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[]
            {
                new EditorBuildSettingsScene(withView, true),
                new EditorBuildSettingsScene(withoutView, true)
            }).ToArray();
        }

        public void Cleanup ()
        {
            string assetFolder = SessionState.GetString(AssetsKey, string.Empty);
            if (string.IsNullOrEmpty(assetFolder))
            {
                return;
            }
            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Where(scene => !scene.path.StartsWith(assetFolder + "/", StringComparison.Ordinal)).ToArray();
            AssetDatabase.DeleteAsset(assetFolder);
            SessionState.EraseString(AssetsKey);
        }

        [SetUp]
        public void SetUp ()
        {
            folder = SessionState.GetString(AssetsKey, string.Empty);
            Assert.That(folder, Is.Not.Empty);
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            foreach (NavigationHost host in hosts)
            {
                await host.ShutdownAsync();
            }
            hosts.Clear();
        });

        [UnityTest]
        public IEnumerator Repeated_scene_loads_own_distinct_views_and_unload_only_the_removed_screen () => UniTask.ToCoroutine(async () =>
        {
            string path = ScenePath(true);
            Scene existing = SceneManager.GetActiveScene();
            List<RuntimeTestView> views = new();
            Receiver progress = new();
            NavigationHost host = CreateHost(path, views);
            await host.StartAsync(new Page(), new NavigationOptions { Transition = Observe(progress, progress.Report) });
            await host.Client.PushAsync(host.Root, Destination.For(new Page()), new NavigationOptions { Transition = Observe(progress, progress.Report) });

            Assert.That(views.Count, Is.EqualTo(2));
            Assert.That(views[0].gameObject.scene.handle, Is.Not.EqualTo(views[1].gameObject.scene.handle));
            Assert.That(progress.Samples.Select(sample => sample.WorkId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(progress.Samples.Count(sample => sample.Value.IsDone), Is.EqualTo(2));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(existing));
            await host.Client.BackAsync(host.Root);
            Assert.That(views[0] != null, Is.True);
            Assert.That(views[1] == null, Is.True);
            Assert.That(LoadedScenes(path), Is.EqualTo(1));
            await host.ShutdownAsync();
            Assert.That(views.All(view => view == null), Is.True);
            Assert.That(existing.IsValid() && existing.isLoaded, Is.True);
            Assert.That(LoadedScenes(path), Is.EqualTo(0));
        });

        [UnityTest]
        public IEnumerator Cancellation_during_native_loading_waits_for_owned_scene_cleanup () => UniTask.ToCoroutine(async () =>
        {
            string path = ScenePath(true);
            using CancellationTokenSource cancellation = new();
            Receiver progress = new()
            {
                OnSample = () => cancellation.Cancel()
            };
            NavigationHost host = CreateHost(path, new List<RuntimeTestView>());
            bool cancelled = false;
            try
            {
                await host.StartAsync(new Page(), new NavigationOptions { Transition = Observe(progress, progress.Report) }, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            Assert.That(cancelled, Is.True);
            Assert.That(host.State.Current.Entries, Is.Empty);
            Assert.That(LoadedScenes(path), Is.EqualTo(0));
            Assert.That(progress.Samples.Any(sample => sample.Phase == NavigationPhase.Prepare && sample.Value.IsDone), Is.False);
            Assert.That(progress.Samples.Any(sample => sample.Phase == NavigationPhase.Cleanup && sample.Value.IsDone), Is.True);
        });

        [UnityTest]
        public IEnumerator Loading_overlay_receives_scene_unload_progress_before_settling_and_input_opens_afterward () => UniTask.ToCoroutine(async () =>
        {
            string path = ScenePath(true);
            List<RuntimeTestView> views = new();
            NavigationHost host = CreateHost(path, views);
            await host.StartAsync(new Page());
            NavigationEntryId departing = host.State.Current.GetRegion(host.Root).Entries.Single();
            GameObject overlay = new("Loading overlay", typeof(CanvasGroup));
            LoadingEffect effect = new(overlay.GetComponent<CanvasGroup>());
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            Task transition = host.Client.ReplaceAsync(host.Root, Destination.For(new Page()), new NavigationOptions
            {
                Transition = Observe(effect, effect.Report)
            });
            try
            {
                await UniTask.WaitUntil(() => effect.Settling, cancellationToken: timeout.Token);
                Assert.That(views[0] == null, Is.True);
                Assert.That(LoadedScenes(path), Is.EqualTo(1));
                Assert.That(effect.Opacity.alpha, Is.EqualTo(1));
                Assert.That(views[1].GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.False);
                Assert.That(transition.IsCompleted, Is.False);
                ProgressUpdate<SceneProgress> released = effect.Samples.Last(sample => sample.Phase == NavigationPhase.Cleanup);
                Assert.That(released.EntryId, Is.EqualTo(departing));
                Assert.That(released.Value.Fraction, Is.EqualTo(1));
                Assert.That(released.Value.IsDone, Is.True);
                Assert.That(released.Value.ScenePath, Is.EqualTo(path));
                Assert.That(effect.Samples.Select(sample => sample.OperationId).Distinct().Count(), Is.EqualTo(1));
                Assert.That(effect.Samples.Any(sample => sample.Phase == NavigationPhase.Prepare && sample.WorkId != released.WorkId), Is.True);
                effect.Finish.TrySetResult(true);
                await transition;
                Assert.That(effect.Opacity.alpha, Is.EqualTo(0));
                Assert.That(views[1].GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
            }
            finally
            {
                effect.Finish.TrySetResult(true);
                try
                {
                    await transition;
                }
                finally
                {
                    UnityEngine.Object.Destroy(overlay);
                }
            }
        });

        [UnityTest]
        public IEnumerator Initial_overlay_displays_scene_and_initialization_progress_before_revealing_and_activating () => UniTask.ToCoroutine(async () =>
        {
            GameObject overlay = new("Initial overlay", typeof(Canvas), typeof(CanvasGroup));
            overlay.SetActive(false);
            overlay.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasViewAdapter adapter = overlay.AddComponent<CanvasViewAdapter>();
            SerializedObject configuration = new(adapter);
            configuration.FindProperty("presentBeforeNavigation").boolValue = true;
            configuration.ApplyModifiedPropertiesWithoutUndo();
            GameObject statusObject = new("Status", typeof(RectTransform), typeof(Text));
            statusObject.transform.SetParent(overlay.transform, false);
            Text status = statusObject.GetComponent<Text>();
            CanvasGroup opacity = overlay.GetComponent<CanvasGroup>();
            opacity.alpha = 1;
            overlay.SetActive(true);
            Assert.That(overlay.GetComponent<Canvas>().enabled, Is.True);
            ResourceLifetime external = new();
            ResourceReference<CanvasViewAdapter> overlayReference = external.Reference(adapter);
            ProgressInput<string> initializationStatus = new("initialization.status");
            ProgressDefinition<string> definition = ProgressDefinition.Create(() => "Starting")
                .Reduce(UnityProgress.SceneLoading, (_, update) => update.Value.IsDone ? "Scene ready" : "Loading scene")
                .Reduce(initializationStatus, (_, update) => update.Value)
                .Build();
            TaskCompletionSource<bool> finishInitialization = new();
            bool initializing = false;
            bool active = false;
            List<string> displayed = new();
            RuntimeTestView? screenView = null;
            LoadingEffect effect = new(opacity);
            ScreenDefinition<Page> screen = new(async (creation, token) =>
            {
                screenView = await creation.AcquireScreenAsync<RuntimeTestView>(new SceneAcquisition(ScenePath(true)), token);
                return new Handler
                {
                    Initialize = async (context, _) =>
                    {
                        context.Progress.GetReporter(initializationStatus).Report("Initializing game");
                        initializing = true;
                        await finishInitialization.Task;
                        context.Progress.GetReporter(initializationStatus).Report("Ready");
                    },
                    Activate = () =>
                    {
                        Assert.That(opacity.alpha, Is.Zero);
                        active = true;
                    }
                };
            });
            NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens =>
                screens.Register(RouteEntryOperations.Reset, LowerPresentationPolicy.HideAndRetain, screen)));
            hosts.Add(host);
            NavigationTransition reveal = NavigationTransition.Create(NavigationTransitionScope.Host, definition, async (context, source, token) =>
            {
                context.RegisterExistingViewAdapter(await context.Lifetime.BorrowAsync(overlayReference, token));
                context.ObserveProgress(source, value =>
                {
                    Assert.That(opacity.alpha, Is.EqualTo(1));
                    status.text = value;
                    displayed.Add(value);
                });
                return effect;
            }, endTiming: TransitionEndTiming.AfterResourceRelease);
            Task start = host.StartAsync(new Page(), new NavigationOptions { Transition = reveal });
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            try
            {
                await UniTask.WaitUntil(() => initializing, cancellationToken: timeout.Token);
                Assert.That(status.text, Is.EqualTo("Initializing game"));
                Assert.That(displayed, Does.Contain("Scene ready"));
                Assert.That(screenView!.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.False);
                Assert.That(active, Is.False);
                Assert.That(start.IsCompleted, Is.False);
                finishInitialization.SetResult(true);
                await UniTask.WaitUntil(() => effect.Settling, cancellationToken: timeout.Token);
                Assert.That(status.text, Is.EqualTo("Ready"));
                Assert.That(opacity.alpha, Is.EqualTo(1));
                Assert.That(active, Is.False);
                effect.Finish.SetResult(true);
                await start;
                Assert.That(active, Is.True);
                Assert.That(screenView.GetComponent<CanvasViewAdapter>().Presentation.InputEnabled, Is.True);
                await host.ShutdownAsync();
                Assert.That(overlay != null, Is.True);
            }
            finally
            {
                finishInitialization.TrySetResult(true);
                effect.Finish.TrySetResult(true);
                try
                {
                    await start;
                }
                finally
                {
                    await host.ShutdownAsync();
                    await external.EndAsync();
                    UnityEngine.Object.Destroy(overlay);
                }
            }
        });

        [UnityTest]
        public IEnumerator Missing_scene_view_releases_the_loaded_scene_without_committing_history () => UniTask.ToCoroutine(async () =>
        {
            string path = ScenePath(false);
            NavigationHost host = CreateHost(path, new List<RuntimeTestView>());
            bool failed = false;
            try
            {
                await host.StartAsync(new Page());
            }
            catch (NavigationException)
            {
                failed = true;
            }
            Assert.That(failed, Is.True);
            Assert.That(host.State.Current.Entries, Is.Empty);
            Assert.That(LoadedScenes(path), Is.EqualTo(0));
        });

        private NavigationHost CreateHost (string path, List<RuntimeTestView> views)
        {
            ScreenDefinition<Page> screen = new(async (creation, token) =>
            {
                RuntimeTestView view = await creation.AcquireScreenAsync<RuntimeTestView>(new SceneAcquisition(path), token);
                views.Add(view);
                return new Handler();
            }, ScreenInstancePolicy.Multiple);
            NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens =>
                screens.Register(RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace, LowerPresentationPolicy.SuspendActivity, screen)));
            hosts.Add(host);
            return host;
        }

        private string ScenePath (bool withView) => folder + (withView ? "/Screen.unity" : "/Empty.unity");

        private string CreateScene (bool withView)
        {
            string path = ScenePath(withView);
            Scene active = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                if (withView)
                {
                    GameObject instance = new("Screen", typeof(RectTransform), typeof(Canvas));
                    SceneManager.MoveGameObjectToScene(instance, scene);
                    instance.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                    CanvasViewAdapter adapter = instance.AddComponent<CanvasViewAdapter>();
                    ScreenPresentation presentation = instance.AddComponent<ScreenPresentation>();
                    instance.AddComponent<RuntimeTestView>();
                    SerializedObject serialized = new(presentation);
                    SerializedProperty adapters = serialized.FindProperty("views");
                    adapters.arraySize = 1;
                    adapters.GetArrayElementAtIndex(0).objectReferenceValue = adapter;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                return path;
            }
            finally
            {
                SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static int LoadedScenes (string path)
        {
            int count = 0;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                if (SceneManager.GetSceneAt(index).path == path)
                {
                    count++;
                }
            }
            return count;
        }

        private static NavigationTransition Observe (INavigationTransitionEffect effect, Action<ProgressUpdate<SceneProgress>> receive)
        {
            ProgressDefinition<ProgressUpdate<SceneProgress>?> definition = ProgressDefinition.Create(() => (ProgressUpdate<SceneProgress>?)null)
                .Reduce(UnityProgress.SceneLoading, (_, update) => update)
                .Reduce(UnityProgress.SceneUnloading, (_, update) => update)
                .Build();
            return NavigationTransition.Create(NavigationTransitionScope.Region, definition, (context, source, _) =>
            {
                context.ObserveProgress(source, update =>
                {
                    if (update.HasValue)
                    {
                        receive(update.Value);
                    }
                });
                return new ValueTask<INavigationTransitionEffect>(effect);
            }, endTiming: TransitionEndTiming.AfterResourceRelease);
        }

        private sealed record Page : Route;

        private sealed class Handler : IScreenLifecycleHandler<Page>
        {
            public Func<ScreenInitializationContext, CancellationToken, ValueTask>? Initialize { get; set; }
            public Action? Activate { get; set; }
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => Initialize?.Invoke(initialization, cancellationToken) ?? default;
            public ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
            public ValueTask ActivateAsync (Page route, ScreenActivityContext activity)
            {
                Activate?.Invoke();
                return default;
            }
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;
        }

        private sealed class Receiver : INavigationTransitionEffect
        {
            public List<ProgressUpdate<SceneProgress>> Samples { get; } = new();
            public Action? OnSample { get; set; }
            public void Report (ProgressUpdate<SceneProgress> progress)
            {
                Samples.Add(progress);
                OnSample?.Invoke();
            }
            public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken) => default;
            public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
            public ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
            public ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken) => default;
        }

        private sealed class LoadingEffect : INavigationTransitionEffect
        {
            public LoadingEffect (CanvasGroup opacity) => Opacity = opacity;
            public CanvasGroup Opacity { get; }
            public List<ProgressUpdate<SceneProgress>> Samples { get; } = new();
            public TaskCompletionSource<bool> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Settling { get; private set; }
            public void Report (ProgressUpdate<SceneProgress> progress)
            {
                Assert.That(Opacity.alpha, Is.EqualTo(1));
                Samples.Add(progress);
            }
            public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken)
            {
                Opacity.alpha = 1;
                return default;
            }
            public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
            public ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
            public async ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken)
            {
                Settling = true;
                await Finish.Task;
                Opacity.alpha = 0;
            }
        }
    }
}
#endif
