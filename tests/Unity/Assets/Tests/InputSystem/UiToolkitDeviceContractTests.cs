using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Unity.UIToolkit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;
using PointerType = UnityEngine.UIElements.PointerType;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class UiToolkitDeviceContractTests
    {
        private readonly List<Object> objects = new();
        private readonly List<InputDevice> devices = new();
        private UiToolkitPresentationHost host = null!;
        private InputSystemUIInputModule module = null!;
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
            GameObject events = Track(new GameObject("UI input test", typeof(EventSystem)));
            module = events.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            module.moveRepeatDelay = 0.05f;
            module.moveRepeatRate = 0.025f;
            PanelSettings template = Track(ScriptableObject.CreateInstance<PanelSettings>());
            template.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            host = new UiToolkitPresentationHost(template);
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
#endif
            foreach (Object value in objects)
            {
                if (value != null)
                {
                    Object.Destroy(value);
                }
            }
            objects.Clear();
            await UniTask.NextFrame();
            host.Dispose();
            foreach (InputDevice device in devices)
            {
                InputSystem.RemoveDevice(device);
            }
            devices.Clear();
        });

        [UnityTest]
        public IEnumerator Gamepad_navigation_cannot_enter_the_background_after_a_raw_focus_request () => UniTask.ToCoroutine(async () =>
        {
            Gamepad pad = Device<Gamepad>();
            UiToolkitViewAdapter back = Create();
            UiToolkitViewAdapter front = Create();
            Button old = AddButton(back);
            Button current = AddButton(front);
            AddButton(front);
            await Apply(front, back);
            current.Focus();
            await UniTask.NextFrame();
            int foregroundMoves = 0;
            int backgroundMoves = 0;
            int backgroundFocus = 0;
            front.View.ContentRoot.RegisterCallback<NavigationMoveEvent>(_ => foregroundMoves++, TrickleDown.TrickleDown);
            back.View.ContentRoot.RegisterCallback<NavigationMoveEvent>(_ => backgroundMoves++, TrickleDown.TrickleDown);
            old.RegisterCallback<FocusInEvent>(_ => backgroundFocus++);
            old.Focus();
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
            await UniTask.Delay(100, ignoreTimeScale: true);
            Assert.That(foregroundMoves, Is.GreaterThan(0));
            Assert.That(backgroundMoves, Is.Zero);
            Assert.That(backgroundFocus, Is.Zero);
            Assert.That(old.enabledSelf, Is.True);
        });

        [UnityTest]
        public IEnumerator Keyboard_submit_stays_on_the_foreground_after_a_background_raw_focus_request () => UniTask.ToCoroutine(async () =>
        {
            Keyboard keyboard = Device<Keyboard>();
            UiToolkitViewAdapter back = Create();
            UiToolkitViewAdapter front = Create();
            Button old = AddButton(back);
            Button current = AddButton(front);
            await UniTask.NextFrame();
            await Apply(front, back);
            current.Focus();
            await UniTask.NextFrame();
            int frontClicks = 0;
            int backClicks = 0;
            int backFocus = 0;
            current.clicked += () => frontClicks++;
            old.clicked += () => backClicks++;
            old.RegisterCallback<FocusInEvent>(_ => backFocus++);
            old.Focus();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(frontClicks, Is.EqualTo(1));
            Assert.That(backClicks, Is.Zero);
            Assert.That(backFocus, Is.Zero);
            Assert.That(old.enabledSelf, Is.True);
        });

        [UnityTest]
        public IEnumerator Mouse_and_touch_press_cannot_enter_the_blocked_screen () => UniTask.ToCoroutine(async () =>
        {
            Mouse mouse = Device<Mouse>();
            Touchscreen touchscreen = Device<Touchscreen>();
            UiToolkitViewAdapter back = Create();
            UiToolkitViewAdapter front = Create();
            Button old = AddButton(back);
            Button current = AddButton(front);
            await UniTask.NextFrame();
            await Apply(front, back);
            int oldInputs = 0;
            int mouseInputs = 0;
            int touchInputs = 0;
            old.RegisterCallback<PointerDownEvent>(_ => oldInputs++);
            old.RegisterCallback<FocusInEvent>(_ => oldInputs++);
            current.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.pointerType == PointerType.mouse)
                {
                    mouseInputs++;
                }
                if (evt.pointerType == PointerType.touch)
                {
                    touchInputs++;
                }
            }, TrickleDown.TrickleDown);
            Vector2 screenPoint = new(20, Screen.height - 10);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPoint }.WithButton(MouseButton.Left));
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(mouseInputs, Is.GreaterThan(0));
            Assert.That(oldInputs, Is.Zero);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPoint });
            await UniTask.NextFrame();
            InputSystem.QueueStateEvent(touchscreen, new TouchState
            {
                touchId = 1, position = screenPoint, phase = UnityEngine.InputSystem.TouchPhase.Began,
            });
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(touchInputs, Is.GreaterThan(0));
            Assert.That(oldInputs, Is.Zero);
            Assert.That(old.enabledSelf, Is.True);
        });

        [UnityTest]
        public IEnumerator Captured_mouse_press_ends_when_its_screen_closes_and_cannot_continue_in_the_background () => UniTask.ToCoroutine(async () =>
        {
            Mouse mouse = Device<Mouse>();
            UiToolkitViewAdapter first = Create();
            UiToolkitViewAdapter second = Create();
            Button firstButton = AddButton(first);
            Button secondButton = AddButton(second);
            await Apply(first);
            await UniTask.NextFrame();
            int firstClicks = 0;
            int secondClicks = 0;
            int lateInputs = 0;
            bool closed = false;
            firstButton.clicked += () => firstClicks++;
            secondButton.clicked += () => secondClicks++;
            firstButton.RegisterCallback<PointerMoveEvent>(_ =>
            {
                if (closed)
                {
                    lateInputs++;
                }
            }, TrickleDown.TrickleDown);
            firstButton.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (closed)
                {
                    lateInputs++;
                }
            }, TrickleDown.TrickleDown);
            Vector2 position = new(20, Screen.height - 10);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(firstButton.HasPointerCapture(PointerId.mousePointerId), Is.True);
            await Apply(second, first);
            closed = true;
            Assert.That(firstButton.HasPointerCapture(PointerId.mousePointerId), Is.False);
            Assert.That(firstButton.enabledSelf, Is.True);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position + new Vector2(2, 0) }.WithButton(MouseButton.Left));
            await UniTask.NextFrame();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(lateInputs, Is.Zero);
            Assert.That(firstClicks, Is.Zero);
            Assert.That(secondClicks, Is.Zero);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            await UniTask.NextFrame();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(secondClicks, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Captured_touch_ends_when_its_screen_closes_and_cannot_continue_in_the_background () => UniTask.ToCoroutine(async () =>
        {
            Touchscreen touch = Device<Touchscreen>();
            UiToolkitViewAdapter first = Create();
            UiToolkitViewAdapter second = Create();
            Button firstButton = AddButton(first);
            AddButton(second);
            await Apply(first);
            int pointer = -1;
            int lateInputs = 0;
            int clicks = 0;
            bool closed = false;
            firstButton.RegisterCallback<PointerDownEvent>(evt => pointer = evt.pointerId, TrickleDown.TrickleDown);
            firstButton.RegisterCallback<PointerMoveEvent>(_ =>
            {
                if (closed)
                {
                    lateInputs++;
                }
            }, TrickleDown.TrickleDown);
            firstButton.RegisterCallback<PointerUpEvent>(_ =>
            {
                if (closed)
                {
                    lateInputs++;
                }
            }, TrickleDown.TrickleDown);
            firstButton.clicked += () => clicks++;
            Vector2 position = new(20, Screen.height - 10);
            InputSystem.QueueStateEvent(touch, new TouchState
            {
                touchId = 1, position = position, phase = UnityEngine.InputSystem.TouchPhase.Began,
            });
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(pointer, Is.GreaterThanOrEqualTo(PointerId.touchPointerIdBase));
            Assert.That(firstButton.HasPointerCapture(pointer), Is.True);
            await Apply(second, first);
            closed = true;
            Assert.That(firstButton.HasPointerCapture(pointer), Is.False);
            InputSystem.QueueStateEvent(touch, new TouchState
            {
                touchId = 1, position = position + new Vector2(2, 0), phase = UnityEngine.InputSystem.TouchPhase.Moved,
            });
            await UniTask.NextFrame();
            InputSystem.QueueStateEvent(touch, new TouchState
            {
                touchId = 1, position = position, phase = UnityEngine.InputSystem.TouchPhase.Ended,
            });
            await UniTask.NextFrame();
            await UniTask.NextFrame();
            Assert.That(lateInputs, Is.Zero);
            Assert.That(clicks, Is.Zero);
            Assert.That(firstButton.enabledSelf, Is.True);
        });

        private UiToolkitViewAdapter Create ()
        {
            GameObject value = Track(new GameObject("UI device screen"));
            UiToolkitViewAdapter adapter = value.AddComponent<UiToolkitViewAdapter>();
            host.Connect(adapter);
            return adapter;
        }

        private static Button AddButton (UiToolkitViewAdapter adapter)
        {
            Button button = new();
            button.style.height = 60;
            adapter.View.ContentRoot.Add(button);
            return button;
        }

        private async System.Threading.Tasks.ValueTask Apply (UiToolkitViewAdapter target, params UiToolkitViewAdapter[] background)
        {
            // Compose the real native input backend before exercising navigation.
            // Unity creates the EventSystem panel bridges during its own update.
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            await UniTask.WaitUntil(() => background.Append(target).All(view =>
                module.GetComponentsInChildren<PanelEventHandler>().Any(handler =>
                    ReferenceEquals(handler.panel, view.View.ContentRoot.panel))), cancellationToken: timeout.Token);
            List<ViewPresentationChange> changes = new();
            foreach (UiToolkitViewAdapter view in background)
            {
                changes.Add(new ViewPresentationChange(view, new ViewPresentation(true, false, changes.Count)));
            }
            changes.Add(new ViewPresentationChange(target, new ViewPresentation(true, true, changes.Count)));
            await host.ApplyAsync(new ViewPresentationChangeSet(changes));
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
    }
}
