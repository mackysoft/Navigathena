#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Integration;
using MackySoft.Navigathena.Samples.Common.Confirmation;
using MackySoft.Navigathena.Unity.UGUI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class ConfirmationPopupContractTests
    {
        private readonly List<GameObject> objects = new();
        private EventSystem events = null!;
        private EventSystem? previousEvents;
        private ConfirmationPopupView view = null!;
        private ObservedPopup popup = null!;
        private NavigationHost host = null!;

        [SetUp]
        public void SetUp ()
        {
            popup = null!;
            previousEvents = EventSystem.current;
            GameObject eventObject = Track(new GameObject("Confirmation events", typeof(EventSystem)));
            events = eventObject.GetComponent<EventSystem>();
            EventSystem.current = events;

            CanvasViewAdapter adapter = Canvas("Confirmation popup");
            view = adapter.gameObject.AddComponent<ConfirmationPopupView>();
            SerializedObject configuration = new(view);
            foreach (string name in new[] { "title", "message", "acceptText", "declineText" })
            {
                GameObject text = new(name, typeof(RectTransform), typeof(Text));
                text.transform.SetParent(view.transform, false);
                configuration.FindProperty(name).objectReferenceValue = text.GetComponent<Text>();
            }
            foreach (string name in new[] { "accept", "decline", "close" })
            {
                GameObject control = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
                control.transform.SetParent(view.transform, false);
                configuration.FindProperty(name).objectReferenceValue = control.GetComponent<Button>();
            }
            configuration.ApplyModifiedPropertiesWithoutUndo();

            ScreenCatalog catalog = ScreenCatalog.Build(screens =>
            {
                screens.Register(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve,
                    new ScreenDefinition<Page>((_, _) => new ValueTask<IScreenLifecycleHandler<Page>>(new PagePresenter()),
                        ScreenInstancePolicy.Multiple));
                screens.Register<ConfirmationRoute, bool>(RouteEntryOperations.Push, LowerPresentationPolicy.SuspendActivity,
                    (creation, _) =>
                    {
                        creation.ConnectPresentation(new ScreenPresentationBinding(new[] { adapter }));
                        popup = new ObservedPopup(new ConfirmationPresenter(view, events));
                        return new ValueTask<IScreenLifecycleHandler<ConfirmationRoute, bool>>(popup);
                    });
            });
            host = NavigationHost.Create(catalog, new NavigationHostOptions
            {
                DefaultBlocker = new BlockerDefinition((_, _) =>
                    new ValueTask<IBlockerPresenter>(new Blocker(Canvas("Confirmation blocker"))))
            });
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            await host.ShutdownAsync();
            foreach (GameObject value in objects)
            {
                if (value != null)
                {
                    Object.Destroy(value);
                }
            }
            objects.Clear();
            await UniTask.NextFrame();
            if (previousEvents != null)
            {
                EventSystem.current = previousEvents;
            }
        });

        [UnityTest]
        public IEnumerator Accept_is_ignored_while_covered_and_returns_true_after_foreground_resumes () => CheckOperation(0);

        [UnityTest]
        public IEnumerator Decline_is_ignored_while_covered_and_returns_false_after_foreground_resumes () => CheckOperation(1);

        [UnityTest]
        public IEnumerator Close_is_ignored_while_covered_and_dismisses_after_foreground_resumes () => CheckOperation(2);

        private IEnumerator CheckOperation (int operation) => UniTask.ToCoroutine(async () =>
        {
            await host.StartAsync(new Page());
            Task<bool> answer = host.Client.InvokeAsync(host.Root, new ConfirmationRoute("Question", "Continue?", "Accept", "Decline"));
            await UniTask.WaitUntil(() => popup != null && popup.Foregrounds.Count == 1).Timeout(TimeSpan.FromSeconds(10));
            ScreenForegroundToken initial = popup.Foregrounds[0];
            Assert.That(events.currentSelectedGameObject, Is.SameAs(view.Decline.gameObject));
            Button control = operation switch
            {
                0 => view.Accept,
                1 => view.Decline,
                _ => view.Close
            };

            await host.Client.PushAsync(host.Root, Destination.For(new Page()));
            Assert.That(popup.Activity.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(initial.IsValid, Is.False);

            // Exercise the application callback, independently of native hit testing and gesture state.
            control.onClick.Invoke();
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(answer.IsCompleted, Is.False);

            await host.Client.BackAsync(host.Root);
            Assert.That(popup.Activations, Is.EqualTo(1));
            Assert.That(events.currentSelectedGameObject, Is.SameAs(view.Decline.gameObject));
            Assert.That(initial.IsValid, Is.False);
            Assert.That(popup.Foregrounds.Count, Is.EqualTo(2));
            Assert.That(popup.Foregrounds[1].IsValid, Is.True);
            control.onClick.Invoke();
            if (operation < 2)
            {
                Assert.That(await answer.AsUniTask().Timeout(TimeSpan.FromSeconds(10)), Is.EqualTo(operation == 0));
            }
            else
            {
                try
                {
                    await answer.AsUniTask().Timeout(TimeSpan.FromSeconds(10));
                    Assert.Fail("Closing the popup must dismiss the result call.");
                }
                catch (OperationCanceledException)
                {
                }
            }
        });

        private CanvasViewAdapter Canvas (string name)
        {
            GameObject value = Track(new GameObject(name, typeof(Canvas)));
            value.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return value.AddComponent<CanvasViewAdapter>();
        }

        private GameObject Track (GameObject value)
        {
            objects.Add(value);
            return value;
        }

        private sealed record Page : Route;

        private sealed class PagePresenter : IScreenLifecycleHandler<Page>
        {
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;
            public ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
            public ValueTask ActivateAsync (Page route, ScreenActivityContext activity) => default;
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;
        }

        private sealed class ObservedPopup : IScreenLifecycleHandler<ConfirmationRoute, bool>, IScreenForegroundLifecycleHandler
        {
            private readonly ConfirmationPresenter presenter;
            public ObservedPopup (ConfirmationPresenter presenter) => this.presenter = presenter;
            public ScreenActivityContext<bool> Activity { get; private set; } = null!;
            public int Activations { get; private set; }
            public List<ScreenForegroundToken> Foregrounds { get; } = new();
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
                => presenter.InitializeAsync(initialization, cancellationToken);
            public ValueTask PrepareAsync (ConfirmationRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
                => presenter.PrepareAsync(route, preparation, cancellationToken);
            public ValueTask ActivateAsync (ConfirmationRoute route, ScreenActivityContext<bool> activity)
            {
                Activity = activity;
                Activations++;
                return presenter.ActivateAsync(route, activity);
            }
            public ValueTask DeactivateAsync () => presenter.DeactivateAsync();
            public ValueTask TerminateAsync (NavigationProgressReporter progress) => presenter.TerminateAsync(progress);
            public void OnForegroundAvailable (ScreenForegroundToken foreground)
            {
                presenter.OnForegroundAvailable(foreground);
                Foregrounds.Add(foreground);
            }
        }

        private sealed class Blocker : IBlockerPresenter
        {
            private readonly CanvasViewAdapter view;
            public Blocker (CanvasViewAdapter view) => this.view = view;
            public ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken)
            {
                preparation.RegisterViewAdapter(view);
                return default;
            }
            public void SetScreenContext (BlockerScreenContext? context) { }
            public ValueTask TerminateAsync () => default;
        }
    }
}
#endif
