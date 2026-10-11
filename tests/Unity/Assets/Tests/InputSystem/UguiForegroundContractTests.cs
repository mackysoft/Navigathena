using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Integration;
using MackySoft.Navigathena.Unity.UGUI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class UguiForegroundContractTests
    {
        private readonly List<Object> objects = new();
        private readonly List<InputDevice> devices = new();
        private readonly List<Screen> screens = new();
        private EventSystem events = null!;
        private NavigationHost host = null!;
        private InputSettings.BackgroundBehavior previousBackgroundBehavior;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
#endif

        [SetUp]
        public void SetUp ()
        {
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            GameObject eventObject = Track(new GameObject("uGUI foreground events", typeof(EventSystem)));
            events = eventObject.GetComponent<EventSystem>();
            InputSystemUIInputModule module = eventObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            EventSystem.current = events;

            RegionDefinitionId root = new("root");
            NavigationDefinition definition = NavigationDefinition.Build(root, RegionCompositionMode.Layered,
                region => region.AddRoute<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(root, registration =>
                registration.RegisterScreen(new ScreenDefinition<Page>((creation, _) =>
                {
                    CanvasViewAdapter view = Canvas("uGUI page");
                    float y = screens.Count == 0 ? -100 : 100;
                    Button first = Button(view.transform, new Vector2(-60, y));
                    Button second = Button(view.transform, new Vector2(60, y));
                    first.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = second };
                    second.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = first };
                    Screen screen = creation.Lifetime.CreateOwned(() => new Screen(events, first, second));
                    screens.Add(screen);
                    creation.ConnectPresentation(new ScreenPresentationBinding(new[] { view }));
                    return new ValueTask<IScreenLifecycleHandler<Page>>(screen);
                }, ScreenInstancePolicy.Multiple))));
            host = NavigationHost.Create(catalog, new NavigationHostOptions
            {
                DefaultBlocker = new BlockerDefinition((preparation, _) =>
                {
                    CanvasViewAdapter view = Canvas("uGUI modal blocker");
                    GameObject surface = Track(new GameObject("Blocker surface", typeof(RectTransform), typeof(Image)));
                    surface.transform.SetParent(view.transform, false);
                    RectTransform rect = surface.GetComponent<RectTransform>();
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    surface.GetComponent<Image>().color = Color.clear;
                    return new ValueTask<IBlockerPresenter>(new Blocker(view));
                })
            });
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            await host.ShutdownAsync();
            foreach (Object value in objects)
            {
                if (value != null)
                {
                    Object.Destroy(value);
                }
            }
            objects.Clear();
            screens.Clear();
            await UniTask.NextFrame();
            foreach (InputDevice device in devices)
            {
                InputSystem.RemoveDevice(device);
            }
            devices.Clear();
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
#endif
        });

        [UnityTest]
        public IEnumerator Foreground_notifications_and_explicit_links_keep_keyboard_and_gamepad_on_the_current_screen () => UniTask.ToCoroutine(async () =>
        {
            Keyboard keyboard = Device<Keyboard>();
            Gamepad gamepad = Device<Gamepad>();
            await host.StartAsync(new Page());
            Screen lower = screens[0];
            ScreenForegroundToken initial = lower.Foregrounds[0];
            Assert.That(events.currentSelectedGameObject, Is.SameAs(lower.First.gameObject));

            await host.Client.PushAsync(host.Root, Destination.For(new Page()));
            Screen popup = screens[1];
            Assert.That(initial.IsValid, Is.False);
            Assert.That(lower.Activity.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(events.currentSelectedGameObject, Is.SameAs(popup.First.gameObject));
            int lowerSelections = lower.Selections;
            await Frames();

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow));
            await Frames();
            Assert.That(events.currentSelectedGameObject, Is.SameAs(popup.Second.gameObject));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.DownArrow));
            await Frames();
            Assert.That(events.currentSelectedGameObject, Is.SameAs(popup.Second.gameObject));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames();

            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadUp));
            await Frames();
            Assert.That(events.currentSelectedGameObject, Is.SameAs(popup.First.gameObject));
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            await Frames();
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadUp));
            await Frames();
            Assert.That(events.currentSelectedGameObject, Is.SameAs(popup.First.gameObject));
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            await Frames();
            Assert.That(popup.Clicks, Is.EqualTo(1));
            Assert.That(lower.Clicks, Is.Zero);
            Assert.That(lower.Selections, Is.EqualTo(lowerSelections));
            Assert.That(lower.First.IsInteractable(), Is.True);
            Assert.That(lower.Second.IsInteractable(), Is.True);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            await Frames();

            await host.Client.BackAsync(host.Root);
            Assert.That(events.currentSelectedGameObject, Is.SameAs(lower.First.gameObject));
            Assert.That(lower.Activations, Is.EqualTo(1));
            Assert.That(lower.Foregrounds.Count, Is.EqualTo(2));
            Assert.That(initial.IsValid, Is.False);
            Assert.That(lower.Foregrounds[1].IsValid, Is.True);
            await host.ShutdownAsync();
            Assert.That(events.currentSelectedGameObject, Is.Null);
        });

        [UnityTest]
        public IEnumerator Modal_blocker_stops_new_mouse_and_touch_clicks_without_disabling_the_lower_controls () => UniTask.ToCoroutine(async () =>
        {
            Mouse mouse = Device<Mouse>();
            Touchscreen touch = Device<Touchscreen>();
            await host.StartAsync(new Page());
            await Frames();
            Screen lower = screens[0];
            Vector2 lowerPoint = RectTransformUtility.WorldToScreenPoint(null, lower.First.transform.position);
            await Click(mouse, lowerPoint);
            Assert.That(lower.Clicks, Is.EqualTo(1));

            await host.Client.PushAsync(host.Root, Destination.For(new Page()));
            await Frames();
            Screen popup = screens[1];
            int lowerSelections = lower.Selections;
            await Click(mouse, lowerPoint);
            await Tap(touch, lowerPoint);
            Assert.That(lower.Clicks, Is.EqualTo(1));
            Assert.That(lower.Selections, Is.EqualTo(lowerSelections));
            Assert.That(popup.Clicks, Is.Zero);
            GameObject? selected = events.currentSelectedGameObject;
            Assert.That(selected == null || selected.transform.IsChildOf(popup.First.transform.parent), Is.True);
            Assert.That(lower.First.IsInteractable(), Is.True);
            Assert.That(lower.Second.IsInteractable(), Is.True);

            Vector2 popupPoint = RectTransformUtility.WorldToScreenPoint(null, popup.First.transform.position);
            await Click(mouse, popupPoint);
            await Tap(touch, popupPoint);
            Assert.That(popup.Clicks, Is.EqualTo(2));
            Assert.That(lower.Clicks, Is.EqualTo(1));
        });

        private CanvasViewAdapter Canvas (string name)
        {
            GameObject value = Track(new GameObject(name, typeof(Canvas)));
            value.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            return value.AddComponent<CanvasViewAdapter>();
        }

        private Button Button (Transform parent, Vector2 position)
        {
            GameObject value = Track(new GameObject("Screen button", typeof(RectTransform), typeof(Image), typeof(Button)));
            value.transform.SetParent(parent, false);
            RectTransform rect = value.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(80, 50);
            rect.anchoredPosition = position;
            Button button = value.GetComponent<Button>();
            button.targetGraphic = value.GetComponent<Image>();
            return button;
        }

        private static async ValueTask Frames ()
        {
            await UniTask.NextFrame();
            await UniTask.NextFrame();
        }

        private static async ValueTask Click (Mouse mouse, Vector2 position)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            await Frames();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            await Frames();
        }

        private static async ValueTask Tap (Touchscreen touchscreen, Vector2 position)
        {
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = 1, position = position, phase = UnityEngine.InputSystem.TouchPhase.Began });
            await Frames();
            InputSystem.QueueStateEvent(touchscreen, new TouchState { touchId = 1, position = position, phase = UnityEngine.InputSystem.TouchPhase.Ended });
            await Frames();
        }

        private T Device<T> () where T : InputDevice, new()
        {
            T device = InputSystem.AddDevice<T>();
            devices.Add(device);
            return device;
        }

        private T Track<T> (T value) where T : Object
        {
            objects.Add(value);
            return value;
        }

        private sealed record Page : Route;

        private sealed class Screen : IScreenLifecycleHandler<Page>, IScreenForegroundLifecycleHandler
        {
            private readonly EventSystem events;
            public Screen (EventSystem events, Button first, Button second)
            {
                this.events = events;
                First = first;
                Second = second;
                foreach (Button button in new[] { first, second })
                {
                    button.onClick.AddListener(() => Clicks++);
                    EventTrigger observer = button.gameObject.AddComponent<EventTrigger>();
                    EventTrigger.Entry selected = new() { eventID = EventTriggerType.Select };
                    selected.callback.AddListener(_ => Selections++);
                    observer.triggers.Add(selected);
                }
            }
            public Button First { get; }
            public Button Second { get; }
            public int Clicks { get; private set; }
            public int Selections { get; private set; }
            public int Activations { get; private set; }
            public ScreenActivityContext Activity { get; private set; } = null!;
            public List<ScreenForegroundToken> Foregrounds { get; } = new();
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;
            public ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;
            public ValueTask ActivateAsync (Page route, ScreenActivityContext activity)
            {
                Activity = activity;
                Activations++;
                return default;
            }
            public ValueTask DeactivateAsync () => default;
            public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;
            public void OnForegroundAvailable (ScreenForegroundToken foreground)
            {
                Foregrounds.Add(foreground);
                events.SetSelectedGameObject(First.gameObject);
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
