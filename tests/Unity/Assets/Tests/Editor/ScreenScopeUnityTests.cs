using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Integration;
using MackySoft.Navigathena.MicrosoftDI;
using MackySoft.Navigathena.Unity.UGUI;
using MackySoft.Navigathena.VContainer;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class ScreenScopeUnityTests
    {
        private static readonly RegionDefinitionId Root = new("root");

        [UnityTest] public IEnumerator Manual_result_screen_restores_its_caller () => CheckCall(0);
        [UnityTest] public IEnumerator Microsoft_DI_result_screen_restores_its_caller () => CheckCall(1);
        [UnityTest] public IEnumerator VContainer_result_screen_restores_its_caller () => CheckCall(2);

        private static IEnumerator CheckCall (int mode) => UniTask.ToCoroutine(async () =>
        {
            GameObject callerObject = new("Caller", typeof(Canvas));
            GameObject answerObject = new("Answer", typeof(Canvas));
            callerObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            answerObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasViewAdapter callerView = callerObject.AddComponent<CanvasViewAdapter>();
            CanvasViewAdapter answerView = answerObject.AddComponent<CanvasViewAdapter>();
            GameService game = new();
            ContainerBuilder builder = new();
            builder.RegisterInstance(game);
            using IObjectResolver parent = builder.Build();
            ScreenDefinition<PopupRoute> caller = new((creation, _) =>
            {
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { callerView }, returnState: ScreenAnimationState.BeforeEnter));
                LocalService service = creation.Resources.CreateOwned(() => new LocalService(game));
                return new(creation.Resources.CreateOwned(() => new PopupPresenter(callerView, game, service)));
            })
            {
                HistoryReturn = new ScreenHistoryReturnOptions { Preparation = ScreenPreparationMode.Always }
            };
            ScreenDefinition<QuestionRoute, bool> question = new((creation, _) =>
            {
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { answerView }, returnState: ScreenAnimationState.BeforeEnter));
                if (mode == 0)
                {
                    return new(creation.Resources.CreateOwned(() => new QuestionPresenter(game, answerView)));
                }
                if (mode == 1)
                {
                    return new(creation.CreateScope(services =>
                    {
                        services.AddSingleton(game);
                        services.AddSingleton(answerView);
                        services.AddScreenLifecycleHandler<QuestionPresenter>();
                    }));
                }
                return new(creation.CreateScope(parent, new QuestionInstaller(answerView)));
            });
            NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, region =>
            {
                region.AddRoute<PopupRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                });
                region.AddRoute<QuestionRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Push;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
                });
            });
            ScreenCatalog catalog = ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(Root, screens =>
            {
                screens.RegisterScreen(caller);
                screens.RegisterScreen(question);
            }));
            NavigationHost host = NavigationHost.Create(catalog);
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            try
            {
                await host.StartAsync(new PopupRoute(3));
                ScreenActivityContext first = game.Presenter.Activity;
                game.Presenter.Selection = 42;
                Task<bool> answer = first.Navigation.InvokeAsync(new QuestionRoute(10));
                await UniTask.WaitUntil(() => game.Question?.Activity != null && answerView.Presentation.InputEnabled).Timeout(TimeSpan.FromSeconds(10));
                Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(game.Question.Cost, Is.EqualTo(10));
                Assert.That(callerView.Presentation.InputEnabled, Is.False);
                QuestionPresenter previous = game.Question;
                await previous.Activity.Navigation.ReloadAsync();
                Assert.That(previous.Disposals, Is.EqualTo(1));
                Assert.That(game.Question, Is.Not.SameAs(previous));
                Assert.That(game.Question.Activity.EntryId, Is.EqualTo(previous.Activity.EntryId));
                Assert.That(answer.IsCompleted, Is.False);
                game.Question.Activity.Call.Complete(false);
                Assert.That(await answer.AsUniTask().Timeout(TimeSpan.FromSeconds(10)), Is.False);
                await Task.Yield();
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                callerObject.name = "Answer applied";
                Assert.That(callerObject.name, Is.EqualTo("Answer applied"));
                Assert.That(game.Question.Disposals, Is.EqualTo(1));
                Assert.That(game.Presenter.Activity.EntryId, Is.EqualTo(first.EntryId));
                Assert.That(game.Presenter.Activity.IsFirstActivation, Is.False);
                Assert.That(game.Presenter.Activity.Reason, Is.EqualTo(ScreenActivationReason.HistoryReturn));
                Assert.That(game.Presenter.Selection, Is.EqualTo(42));
                Assert.That(callerView.Presentation.InputEnabled, Is.True);
            }
            finally
            {
                await host.ShutdownAsync();
                Object.Destroy(callerObject);
                Object.Destroy(answerObject);
                await UniTask.NextFrame();
            }
        });

        public sealed record QuestionRoute : Route<bool>
        {
            public QuestionRoute (int cost) => Cost = cost;
            public int Cost
            {
                get;
            }
        }

        private sealed class QuestionInstaller : IInstaller
        {
            private readonly CanvasViewAdapter view;
            public QuestionInstaller (CanvasViewAdapter view) => this.view = view;
            public void Install (IContainerBuilder builder)
            {
                builder.RegisterInstance(view);
                builder.RegisterScreenLifecycleHandler<QuestionPresenter>();
            }
        }

        public sealed class QuestionPresenter : IScreenLifecycleHandler<QuestionRoute, bool>, IDisposable
        {
            private readonly CanvasViewAdapter view;
            public QuestionPresenter (GameService game, CanvasViewAdapter view)
            {
                game.Question = this;
                this.view = view;
            }
            public int Cost
            {
                get; private set;
            }
            public int Disposals
            {
                get; private set;
            }
            public ScreenActivityContext<bool> Activity { get; private set; } = null!;
            public ValueTask InitializeAsync (CancellationToken token) => default;
            public ValueTask PrepareAsync (QuestionRoute route, ScreenPreparationContext preparation, CancellationToken token)
            {
                Cost = route.Cost;
                return default;
            }
            public ValueTask ActivateAsync (QuestionRoute route, ScreenActivityContext<bool> activity)
            {
                Activity = activity;
                return default;
            }
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync ()
            {
                Assert.That(view != null, Is.True);
                return default;
            }
            public void Dispose () => Disposals++;
        }

        [UnityTest] public IEnumerator Placed_canvas_with_manual_dependencies_reuses_one_screen () => CheckSingle(0);
        [UnityTest] public IEnumerator Placed_canvas_with_Microsoft_DI_reuses_one_scope () => CheckSingle(1);
        [UnityTest] public IEnumerator Placed_canvas_with_VContainer_reuses_one_child_scope () => CheckSingle(2);
        [UnityTest] public IEnumerator Placed_canvas_survives_manual_screen_recreation () => CheckSingle(0, true);
        [UnityTest] public IEnumerator Placed_canvas_survives_Microsoft_DI_scope_recreation () => CheckSingle(1, true);
        [UnityTest] public IEnumerator Placed_canvas_survives_VContainer_child_scope_recreation () => CheckSingle(2, true);

        private static IEnumerator CheckSingle (int mode, bool recreate = false) => UniTask.ToCoroutine(async () =>
        {
            GameObject canvas = new("Placed popup", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasViewAdapter adapter = canvas.AddComponent<CanvasViewAdapter>();
            GameService game = new();
            ServiceCollection application = new();
            application.AddSingleton(game);
            ServiceProvider microsoftParent = application.BuildServiceProvider();
            ContainerBuilder rootBuilder = new();
            rootBuilder.RegisterInstance(game);
            IObjectResolver vcontainerParent = rootBuilder.Build();
            ResourceLifetime lifetime = new();
            ResourceReference<CanvasViewAdapter> reference = lifetime.Reference(adapter);
            int constructions = 0;
            ScreenDefinition<PopupRoute> screen = new(async (creation, token) =>
            {
                constructions++;
                CanvasViewAdapter view = await creation.Resources.BorrowAsync(reference, token);
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { view }, returnState: ScreenAnimationState.BeforeEnter));
                if (mode == 0)
                {
                    LocalService service = creation.Resources.CreateOwned(() => new LocalService(game));
                    return creation.Resources.CreateOwned(() => new PopupPresenter(view, game, service));
                }
                else if (mode == 1)
                {
                    return creation.CreateScope(services =>
                    {
                        services.ImportService<GameService>(microsoftParent);
                        services.AddSingleton(view);
                        services.AddScoped<LocalService>();
                        services.AddScreenLifecycleHandler<PopupPresenter>();
                    });
                }
                else
                {
                    return creation.CreateScope(vcontainerParent, new PopupInstaller(view));
                }
            });
            NavigationHost host = CreateHost(screen);
            try
            {
                Assert.That((await host.Start(new PopupRoute(1)).WaitAsync()).Kind, Is.EqualTo(NavigationResultKind.Committed));
                PopupPresenter presenter = game.Presenter;
                ScreenActivityContext old = presenter.Activity;
                presenter.Selection = 10;
                Assert.That((await old.Navigation.Push(new PopupRoute(2)).WaitAsync()).Kind, Is.EqualTo(NavigationResultKind.Committed));
                Assert.That((await old.Navigation.Back().WaitAsync()).Kind, Is.EqualTo(NavigationResultKind.Rejected));
                Assert.That((await presenter.Activity.Navigation.Back().WaitAsync()).Kind, Is.EqualTo(NavigationResultKind.Committed));
                Assert.That(presenter.Current, Is.EqualTo(1));
                Assert.That(presenter.Selection, Is.EqualTo(10));
                Assert.That(constructions, Is.EqualTo(1));
                Assert.That(adapter.Presentation.InputEnabled, Is.True);
                if (recreate)
                {
                    NavigationEntryId previousVisit = presenter.Activity.EntryId;
                    await presenter.Activity.Navigation.ResetAsync(new PopupRoute(3), new NavigationOptions { RecreateInstance = true });
                    Assert.That(game.Presenter, Is.Not.SameAs(presenter));
                    Assert.That(game.Presenter.Activity.EntryId, Is.Not.EqualTo(previousVisit));
                    Assert.That(game.Presenter.Current, Is.EqualTo(3));
                    Assert.That(game.Presenter.Selection, Is.Zero);
                    Assert.That(game.Events, Is.EqualTo(new[] { "terminate", "presenter.dispose", "service.dispose" }));
                    Assert.That(constructions, Is.EqualTo(2));
                    Assert.That(canvas != null, Is.True);
                    Assert.That(adapter.Presentation.InputEnabled, Is.True);
                    game.Events.Clear();
                }
                await host.ShutdownAsync();
                Assert.That(game.Events, Is.EqualTo(new[] { "terminate", "presenter.dispose", "service.dispose" }));
                Assert.That(canvas != null, Is.True);
            }
            finally
            {
                await host.ShutdownAsync();
                await lifetime.EndAsync();
                await microsoftParent.DisposeAsync();
                vcontainerParent.Dispose();
                Object.Destroy(canvas);
                await UniTask.NextFrame();
            }
        });

        [UnityTest]
        public IEnumerator Failed_VContainer_build_callback_retains_child_ownership_for_cleanup () => UniTask.ToCoroutine(async () =>
        {
            GameObject canvas = new("Popup", typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasViewAdapter view = canvas.AddComponent<CanvasViewAdapter>();
            GameService game = new();
            ContainerBuilder builder = new();
            builder.RegisterInstance(game);
            using IObjectResolver parent = builder.Build();
            ScreenDefinition<PopupRoute> screen = new((creation, _) =>
            {
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { view }, returnState: ScreenAnimationState.BeforeEnter));
                return new(creation.CreateScope(parent, child =>
                {
                    new PopupInstaller(view).Install(child);
                    child.RegisterBuildCallback(_ => throw new InvalidOperationException("User build callback failed."));
                }));
            });
            await using NavigationHost host = CreateHost(screen);
            try
            {
                try
                {
                    await host.StartAsync(new PopupRoute(1));
                    Assert.Fail("A failed container build must fail navigation.");
                }
                catch (NavigationException exception)
                {
                    Assert.That(exception.DestinationCommitted, Is.False);
                    Assert.That(exception.InnerException, Is.Not.Null);
                }
                Assert.That(host.State.Current.Entries.Count, Is.Zero);
                Assert.That(game.Events, Is.EqualTo(new[] { "presenter.dispose", "service.dispose" }));
                await host.ShutdownAsync();
            }
            finally
            {
                Object.Destroy(canvas);
                await UniTask.NextFrame();
            }
        });

        [UnityTest]
        public IEnumerator VContainer_requires_a_lifecycle_entry_point () => CheckInvalidEntryPoint("missing");

        [UnityTest]
        public IEnumerator VContainer_rejects_multiple_lifecycle_entry_points () => CheckInvalidEntryPoint("duplicate");

        [UnityTest]
        public IEnumerator VContainer_rejects_an_incompatible_lifecycle_entry_point () => CheckInvalidEntryPoint("incompatible");

        private static IEnumerator CheckInvalidEntryPoint (string configuration) => UniTask.ToCoroutine(async () =>
        {
            GameService game = new();
            ContainerBuilder builder = new();
            builder.RegisterInstance(game);
            using IObjectResolver parent = builder.Build();
            Cleanup dependency = new();
            ScreenDefinition<PopupRoute> screen = new((creation, _) =>
            {
                creation.Resources.CreateOwned(() => dependency);
                return new(creation.CreateScope(parent, child =>
                {
                    child.Register<LocalService>(Lifetime.Scoped);
                    if (configuration == "duplicate")
                    {
                        child.RegisterScreenLifecycleHandler<PopupPresenter>();
                        child.RegisterScreenLifecycleHandler<LocalService>();
                    }
                    else if (configuration == "incompatible")
                    {
                        child.RegisterScreenLifecycleHandler<LocalService>();
                    }
                }));
            });
            await using NavigationHost host = CreateHost(screen);
            try
            {
                await host.StartAsync(new PopupRoute(1));
                Assert.Fail("Invalid entry points must fail navigation.");
            }
            catch (NavigationException exception)
            {
                Assert.That(exception.DestinationCommitted, Is.False);
                Assert.That(exception.InnerException, Is.TypeOf<NavigationConfigurationException>());
            }
            Assert.That(host.State.Current.Entries.Count, Is.Zero);
            Assert.That(game.Presenter, Is.Null);
            Assert.That(game.Events, Is.Empty);
            Assert.That(dependency.Disposals, Is.EqualTo(1));
        });

        private sealed class Cleanup : IDisposable
        {
            public int Disposals
            {
                get; private set;
            }
            public void Dispose () => Disposals++;
        }

        private static NavigationHost CreateHost (ScreenDefinition<PopupRoute> screen)
        {
            NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root => root.AddRoute<PopupRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Push | RouteEntryOperations.Replace;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            }));
            return NavigationHost.Create(ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(Root, screens => screens.RegisterScreen(screen))));
        }

        public sealed record PopupRoute : Route
        {
            public PopupRoute (int value) => Value = value;
            public int Value
            {
                get;
            }
        }

        public sealed class GameService
        {
            public QuestionPresenter Question { get; set; } = null!;
            public PopupPresenter Presenter { get; set; } = null!;
            public List<string> Events { get; } = new();
        }

        public sealed class LocalService : IDisposable
        {
            private readonly GameService game;
            public LocalService (GameService game) => this.game = game;
            public void Dispose () => game.Events.Add("service.dispose");
        }

        private sealed class PopupInstaller : IInstaller
        {
            private readonly CanvasViewAdapter view;
            public PopupInstaller (CanvasViewAdapter view) => this.view = view;
            public void Install (IContainerBuilder builder)
            {
                builder.RegisterInstance(view);
                builder.Register<LocalService>(Lifetime.Scoped);
                builder.RegisterScreenLifecycleHandler<PopupPresenter>();
            }
        }

        public sealed class PopupPresenter : IScreenLifecycleHandler<PopupRoute>, IScreenStateCapture, IDisposable
        {
            private readonly CanvasViewAdapter view;
            private readonly GameService game;
            public PopupPresenter (CanvasViewAdapter view, GameService game, LocalService service)
            {
                this.view = view;
                this.game = game;
                game.Presenter = this;
            }
            public int Current
            {
                get; private set;
            }
            public int Selection
            {
                get; set;
            }
            public ScreenActivityContext Activity { get; private set; } = null!;
            public object CaptureState () => Selection;
            public ValueTask InitializeAsync (CancellationToken cancellationToken)
            {
                Assert.That(view != null, Is.True);
                return default;
            }
            public ValueTask PrepareAsync (PopupRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
            {
                Current = route.Value;
                Selection = preparation.SavedState is int saved ? saved : 0;
                return default;
            }
            public ValueTask ActivateAsync (PopupRoute route, ScreenActivityContext activity)
            {
                Assert.That(route.Value, Is.EqualTo(Current));
                Activity = activity;
                return default;
            }
            public ValueTask DeactivateAsync ()
            {
                Assert.That(Activity.CancellationToken.IsCancellationRequested, Is.True);
                return default;
            }
            public ValueTask TerminateAsync ()
            {
                Assert.That(view != null, Is.True);
                game.Events.Add("terminate");
                return default;
            }
            public void Dispose ()
            {
                Assert.That(view != null, Is.True);
                game.Events.Add("presenter.dispose");
            }
        }
    }
}
