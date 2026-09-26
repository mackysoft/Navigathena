using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class NativePresentationLifecycleTests
    {
        [UnityTest]
        public IEnumerator Existing_overlay_and_blocker_finish_native_work_before_screen_rebinding () => UniTask.ToCoroutine(async () =>
        {
            List<GameObject> objects = new();
            CanvasViewAdapter CreateCanvas (string name)
            {
                GameObject gameObject = new(name, typeof(Canvas), typeof(CanvasGroup));
                objects.Add(gameObject);
                gameObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                return gameObject.AddComponent<CanvasViewAdapter>();
            }
            CanvasViewAdapter titleView = CreateCanvas("Title");
            CanvasViewAdapter popupView = CreateCanvas("Placed popup");
            CanvasViewAdapter fullView = CreateCanvas("Full screen");
            CanvasViewAdapter blockerView = CreateCanvas("Placed blocker");
            CanvasViewAdapter overlayView = CreateCanvas("Startup overlay");
            ResourceLifetime lifetime = new();
            NativeScreen<TitleRoute> title = new();
            NativeScreen<PopupRoute> popup = new();
            NativeScreen<FullRoute> full = new();
            NativeBlocker blocker = new(blockerView);
            NativeReveal reveal = new(overlayView.GetComponent<CanvasGroup>());
            IObjectResolver parent = new ContainerBuilder().Build();
            int popupConstructions = 0;
            ScreenDefinition<T> Define<T> (CanvasViewAdapter view, NativeScreen<T> handler) where T : Route => new(async (creation, token) =>
            {
                if (ReferenceEquals(handler, popup))
                {
                    popupConstructions++;
                }
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { await creation.Lifetime.BorrowAsync(lifetime.Reference(view), token) }, returnState: ScreenAnimationState.BeforeEnter));
                return handler;
            });
            RegionDefinitionId rootId = new("root");
            NavigationDefinition definition = NavigationDefinition.Build(rootId, RegionCompositionMode.Layered, root =>
            {
                root.AddRoute<TitleRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Reset;
    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
});
                root.AddRoute<PopupRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace;
    route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
});
                root.AddRoute<FullRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push;
    route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
});
            });
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(rootId, screens =>
            {
                screens.RegisterScreen(Define(titleView, title));
                screens.RegisterScreen(Define(popupView, popup));
                screens.RegisterScreen(Define(fullView, full));
            }));
            NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions
            {
                DefaultBlocker = new BlockerDefinition(async (preparation, token) =>
                {
                    await preparation.Lifetime.BorrowAsync(lifetime.Reference(blockerView), token);
                    IObjectResolver scope = preparation.CreateScope(parent, new BlockerInstaller(blocker));
                    return scope.Resolve<NativeBlocker>();
                })
            });
            try
            {
                NavigationTransition startup = new(NavigationTransitionScope.Host, async (preparation, token) =>
                {
                    preparation.RegisterExistingViewAdapter(await preparation.Lifetime.BorrowAsync(lifetime.Reference(overlayView), token));
                    IServiceProvider scope = preparation.CreateScope(services => services.AddScoped(_ => reveal));
                    return scope.GetRequiredService<NativeReveal>();
                });
                await host.StartAsync(new TitleRoute(), new NavigationOptions { Transition = startup });
                Assert.That(reveal.Opacity.alpha, Is.EqualTo(0));
                Assert.That(reveal.Disposals, Is.EqualTo(1));
                Assert.That(titleView.Presentation.InputEnabled, Is.True);

                Assert.That((await title.Activity.Navigation.Push(new PopupRoute(1)).WaitAsync()).DestinationCommitted, Is.True);
                Assert.That(blockerView.Presentation.OutputEnabled, Is.True);
                Assert.That(titleView.Presentation.InputEnabled, Is.False);
                Task<NavigationResult> replacement = popup.Activity.Navigation.Replace(new PopupRoute(2)).WaitAsync();
                await Task.WhenAny(blocker.CancellationObserved.Task, replacement, Task.Delay(5000));
                Assert.That(blocker.CancellationObserved.Task.IsCompleted, Is.True);
                Assert.That(replacement.IsCompleted, Is.False);
                Assert.That(popup.Prepared.Count, Is.EqualTo(1));
                Assert.That(blocker.Context, Is.Null);
                blocker.AnimationFinished.TrySetResult(null);
                Assert.That((await replacement).PresentationStatus, Is.EqualTo(NavigationPresentationStatus.Ready));
                Assert.That(popup.Prepared[1].Value, Is.EqualTo(2));
                Assert.That(popupConstructions, Is.EqualTo(1));
                Assert.That(((PopupRoute)blocker.Context!.Route).Value, Is.EqualTo(2));

                Assert.That((await popup.Activity.Navigation.Push(new FullRoute()).WaitAsync()).DestinationCommitted, Is.True);
                Assert.That(blockerView.Presentation.OutputEnabled, Is.False);
                Assert.That((await full.Activity.Navigation.Back().WaitAsync()).DestinationCommitted, Is.True);
                Assert.That(popup.Prepared.Count, Is.EqualTo(2));
                Assert.That(blockerView.Presentation.InputEnabled, Is.True);
                await host.ShutdownAsync();
                Assert.That(blocker.Terminations, Is.EqualTo(1));
                Assert.That(blocker.Disposals, Is.EqualTo(1));
                Assert.That(objects.TrueForAll(item => item != null), Is.True);
            }
            finally
            {
                blocker.AnimationFinished.TrySetResult(null);
                await host.ShutdownAsync();
                parent.Dispose();
                await lifetime.EndAsync();
                foreach (GameObject gameObject in objects)
                {
                    Object.Destroy(gameObject);
                }
                await UniTask.NextFrame();
            }
        });

        [UnityTest]
        public IEnumerator One_placed_blocker_and_its_scope_are_reused_across_regions () => UniTask.ToCoroutine(async () =>
        {
            List<GameObject> objects = new();
            CanvasViewAdapter Canvas (string name)
            {
                GameObject instance = new(name, typeof(Canvas), typeof(CanvasGroup));
                objects.Add(instance);
                instance.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                return instance.AddComponent<CanvasViewAdapter>();
            }
            CanvasViewAdapter titleView = Canvas("Title");
            CanvasViewAdapter leftView = Canvas("Left popup");
            CanvasViewAdapter rightView = Canvas("Right popup");
            CanvasViewAdapter blockerView = Canvas("Shared placed blocker");
            ResourceLifetime lifetime = new();
            IObjectResolver parent = new ContainerBuilder().Build();
            NativeBlocker blocker = new(blockerView);
            blocker.AnimationFinished.SetResult(null);
            RegionDefinitionId rootId = new("root");
            RegionDefinitionId leftId = new("left");
            RegionDefinitionId rightId = new("right");
            NavigationDefinition definition = NavigationDefinition.Build(rootId, RegionCompositionMode.Layered, root => root.AddRoute<TitleRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                foreach (RegionDefinitionId region in new[] { leftId, rightId })
                {
                    route.AddChildRegion(region, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<PopupRoute>(popup =>
                    {
                        popup.AllowedEntryOperations = RouteEntryOperations.Push;
                        popup.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
                    }));
                }
            }));
            ScreenDefinition<T> Screen<T> (CanvasViewAdapter view) where T : Route => new(async (creation, token) =>
            {
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { await creation.Lifetime.BorrowAsync(lifetime.Reference(view), token) }));
                return new NativeScreen<T>();
            });
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder =>
            {
                builder.RegisterScreens(rootId, screens => screens.RegisterScreen(Screen<TitleRoute>(titleView)));
                builder.RegisterScreens(leftId, screens => screens.RegisterScreen(Screen<PopupRoute>(leftView)));
                builder.RegisterScreens(rightId, screens => screens.RegisterScreen(Screen<PopupRoute>(rightView)));
            });
            int constructions = 0;
            NavigationHost host = NavigationHost.Create(catalog, new NavigationHostOptions
            {
                DefaultBlocker = new BlockerDefinition(async (preparation, token) =>
                {
                    constructions++;
                    await preparation.Lifetime.BorrowAsync(lifetime.Reference(blockerView), token);
                    return preparation.CreateScope(parent, new BlockerInstaller(blocker)).Resolve<NativeBlocker>();
                })
            });
            try
            {
                await host.StartAsync(new TitleRoute());
                RegionInstanceId left = host.State.Current.Regions.Values.Single(region => region.DefinitionId == leftId).Id;
                RegionInstanceId right = host.State.Current.Regions.Values.Single(region => region.DefinitionId == rightId).Id;
                await host.Client.PushAsync(left, Destination.For(new PopupRoute(1)));
                BlockerScreenContext previous = blocker.Context!;
                Assert.That(blockerView.GetComponent<Canvas>().sortingOrder, Is.LessThan(leftView.GetComponent<Canvas>().sortingOrder));
                await host.Client.BackAsync(left);
                await host.Client.PushAsync(right, Destination.For(new PopupRoute(2)));
                Assert.That(constructions, Is.EqualTo(1));
                Assert.That(blocker.Disposals, Is.Zero);
                Assert.That(blocker.Context!.RegionId, Is.EqualTo(right));
                Assert.That((await previous.Navigation.Back().WaitAsync()).Kind, Is.EqualTo(NavigationResultKind.Rejected));
                Assert.That(blockerView.GetComponent<Canvas>().sortingOrder, Is.GreaterThan(titleView.GetComponent<Canvas>().sortingOrder));
                Assert.That(blockerView.GetComponent<Canvas>().sortingOrder, Is.LessThan(rightView.GetComponent<Canvas>().sortingOrder));
                Assert.That(blockerView.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
                await host.ShutdownAsync();
                Assert.That(blocker.Terminations, Is.EqualTo(1));
                Assert.That(blocker.Disposals, Is.EqualTo(1));
                Assert.That(objects.TrueForAll(instance => instance != null), Is.True);
            }
            finally
            {
                await host.ShutdownAsync();
                parent.Dispose();
                await lifetime.EndAsync();
                foreach (GameObject instance in objects)
                {
                    Object.Destroy(instance);
                }
                await UniTask.NextFrame();
            }
        });

        private sealed record TitleRoute : Route;
        private sealed record FullRoute : Route;
        private sealed record PopupRoute : Route
        {
            public PopupRoute (int value) => Value = value;
            public int Value
            {
                get;
            }
        }

        private sealed class NativeScreen<T> : IScreenLifecycleHandler<T> where T : Route
        {
            public List<T> Prepared { get; } = new();
            public ScreenActivityContext Activity { get; private set; } = null!;
            public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
            public ValueTask PrepareAsync (T route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
            {
                Prepared.Add(route);
                return default;
            }
            public ValueTask ActivateAsync (T route, ScreenActivityContext activity)
            {
                Activity = activity;
                return default;
            }
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync () => default;
        }

        private sealed class BlockerInstaller : IInstaller
        {
            private readonly NativeBlocker blocker;
            public BlockerInstaller (NativeBlocker blocker) => this.blocker = blocker;
            public void Install (IContainerBuilder builder) => builder.Register(_ => blocker, Lifetime.Scoped);
        }

        private sealed class NativeBlocker : IBlockerPresenter, IBlockerAnimator, IDisposable
        {
            private readonly CanvasViewAdapter view;
            public NativeBlocker (CanvasViewAdapter view) => this.view = view;
            public TaskCompletionSource<object?> CancellationObserved { get; } = new();
            public TaskCompletionSource<object?> AnimationFinished { get; } = new();
            public BlockerScreenContext? Context
            {
                get; private set;
            }
            public int Terminations
            {
                get; private set;
            }
            public int Disposals
            {
                get; private set;
            }
            public ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken)
            {
                preparation.RegisterViewAdapter(view);
                return default;
            }
            public void SetScreenContext (BlockerScreenContext? context) => Context = context;
            public async ValueTask PlayEnterAsync (CancellationToken cancellationToken)
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(() => CancellationObserved.TrySetResult(null));
                await AnimationFinished.Task;
            }
            public ValueTask PlayExitAsync (CancellationToken cancellationToken) => default;
            public void SetAppearanceImmediately (bool shown) => view.GetComponent<CanvasGroup>().alpha = shown ? 1 : 0;
            public ValueTask TerminateAsync ()
            {
                Terminations++;
                return default;
            }
            public void Dispose () => Disposals++;
        }

        private sealed class NativeReveal : INavigationTransitionEffect, IDisposable
        {
            public NativeReveal (CanvasGroup opacity) => Opacity = opacity;
            public CanvasGroup Opacity
            {
                get;
            }
            public int Disposals
            {
                get; private set;
            }
            public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken)
            {
                Opacity.alpha = 1;
                return default;
            }
            public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
            public async ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken)
            {
                Opacity.alpha = 0.5f;
                await UniTask.NextFrame(cancellationToken: cancellationToken);
                Opacity.alpha = 0;
            }
            public ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken) => default;
            public void Dispose () => Disposals++;
        }
    }
}
