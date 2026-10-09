using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Integration;
using MackySoft.Navigathena.Unity.UIToolkit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class UiToolkitInputContractTests
    {
        private readonly List<Object> objects = new();
        private readonly List<NavigationHost> navigation = new();
        private UiToolkitPresentationHost host = null!;

        [SetUp]
        public void SetUp ()
        {
            PanelSettings template = Track(ScriptableObject.CreateInstance<PanelSettings>());
            template.themeStyleSheet = Resources.Load<ThemeStyleSheet>("NavigathenaTestTheme");
            host = new UiToolkitPresentationHost(template);
        }

        [UnityTearDown]
        public IEnumerator TearDown () => UniTask.ToCoroutine(async () =>
        {
            foreach (NavigationHost value in navigation)
            {
                await value.ShutdownAsync();
            }
            navigation.Clear();
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
        });

        [UnityTest]
        public IEnumerator Background_raw_focus_never_enters_or_disturbs_the_foreground () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter back = Create();
            UiToolkitViewAdapter front = Create();
            Button old = AddButton(back);
            Button current = AddButton(front);
            await Apply(front, back);
            current.Focus();
            await UniTask.NextFrame();
            int backgroundFocus = 0;
            int foregroundBlur = 0;
            old.RegisterCallback<FocusInEvent>(_ => backgroundFocus++);
            current.RegisterCallback<BlurEvent>(_ => foregroundBlur++);
            Button added = AddButton(back);
            added.RegisterCallback<FocusInEvent>(_ => backgroundFocus++);
            old.Focus();
            added.Focus();
            await UniTask.NextFrame();
            int submits = 0;
            current.clicked += () => submits++;
            SendSubmit(front.View.ContentRoot.panel.visualTree);
            Assert.That(backgroundFocus, Is.Zero);
            Assert.That(foregroundBlur, Is.Zero);
            Assert.That(submits, Is.EqualTo(1));
            Assert.That(old.enabledSelf && added.enabledSelf, Is.True);
            Assert.That(old.focusable && added.focusable, Is.True);
        });

        [UnityTest]
        public IEnumerator Revoked_session_is_rejected_before_cleanup_callbacks_and_stays_revoked_after_reopen () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter view = Create();
            Button button = AddButton(view);
            await Apply(view);
            Assert.That(view.View.TryCaptureInput(out UiInputSession old), Is.True);
            bool rejectedDuringCancellation = false;
            using CancellationTokenRegistration callback = old.Revoked.Register(() =>
                rejectedDuringCancellation = !old.IsValid && !view.View.TryCaptureInput(out _));
            await Close(view);
            Assert.That(rejectedDuringCancellation, Is.True);
            await Apply(view);
            Assert.That(old.IsValid, Is.False);
            Assert.That(old.Revoked.IsCancellationRequested, Is.True);
            Assert.That(view.View.TryCaptureInput(out UiInputSession current), Is.True);
            Assert.That(current.IsValid, Is.True);
            Assert.That(button.canGrabFocus, Is.True);
        });

        [UnityTest]
        public IEnumerator Independently_permitted_views_keep_their_native_focus_eligibility () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter first = Create();
            UiToolkitViewAdapter second = Create();
            Button firstButton = AddButton(first);
            Button secondButton = AddButton(second);
            await Apply(first);
            Assert.That(first.View.TryCaptureInput(out UiInputSession session), Is.True);
            await host.ApplyAsync(new ViewPresentationChangeSet(new[]
            {
                new ViewPresentationChange(first, new ViewPresentation(true, true, 0)),
                new ViewPresentationChange(second, new ViewPresentation(true, true, 1)),
            }));
            Assert.That(session.IsValid, Is.True);
            Assert.That(session.Revoked.IsCancellationRequested, Is.False);
            Assert.That(firstButton.canGrabFocus && secondButton.canGrabFocus, Is.True);
            int firstFocus = 0;
            int secondFocus = 0;
            firstButton.RegisterCallback<FocusEvent>(_ => firstFocus++);
            secondButton.RegisterCallback<FocusEvent>(_ => secondFocus++);
            secondButton.Focus();
            await UniTask.NextFrame();
            firstButton.Focus();
            await UniTask.NextFrame();
            Assert.That(firstFocus, Is.EqualTo(1));
            Assert.That(secondFocus, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Order_only_updates_preserve_the_permission_period_and_existing_focus () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter view = Create();
            Button first = AddButton(view);
            Button second = AddButton(view);
            await Apply(view);
            Assert.That(view.View.TryCaptureInput(out UiInputSession session), Is.True);
            first.Focus();
            await UniTask.NextFrame();
            second.Focus();
            await UniTask.NextFrame();
            int blurs = 0;
            second.RegisterCallback<BlurEvent>(_ => blurs++);
            await host.ApplyAsync(new ViewPresentationChangeSet(new[]
            {
                new ViewPresentationChange(view, new ViewPresentation(true, true, 12)),
            }));
            Assert.That(session.IsValid, Is.True);
            Assert.That(blurs, Is.Zero);
            int clicks = 0;
            second.clicked += () => clicks++;
            SendSubmit(view.View.ContentRoot.panel.visualTree);
            Assert.That(clicks, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Native_pending_focus_is_drained_before_permission_revocation () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter view = Create();
            Button first = AddButton(view);
            Button second = AddButton(view);
            await Apply(view);
            Assert.That(view.View.TryCaptureInput(out UiInputSession session), Is.True);
            bool revoked = false;
            int lateFocus = 0;
            using CancellationTokenRegistration callback = session.Revoked.Register(() => revoked = true);
            first.RegisterCallback<FocusInEvent>(_ =>
            {
                if (revoked)
                {
                    lateFocus++;
                }
            });
            second.RegisterCallback<FocusInEvent>(_ =>
            {
                if (revoked)
                {
                    lateFocus++;
                }
            });
            Task closing;
            using (new EventDispatcherGate(view.View.ContentRoot.panel.dispatcher))
            {
                first.Focus();
                closing = Close(view).AsTask();
                second.Focus();
                Assert.That(closing.IsCompleted, Is.False);
                await UniTask.NextFrame();
                Assert.That(closing.IsCompleted, Is.False);
            }
            await closing;
            await UniTask.NextFrame();
            Assert.That(revoked, Is.True);
            Assert.That(lateFocus, Is.Zero);
            Assert.That(first.canGrabFocus || second.canGrabFocus, Is.False);
        });

        [UnityTest]
        public IEnumerator Empty_foreground_keeps_nonposition_input_from_falling_through () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter back = Create();
            UiToolkitViewAdapter front = Create();
            Button button = AddButton(back);
            int focus = 0;
            int clicks = 0;
            button.RegisterCallback<FocusInEvent>(_ => focus++);
            button.clicked += () => clicks++;
            await Apply(front, back);
            using KeyDownEvent tab = KeyDownEvent.GetPooled('\t', KeyCode.Tab, EventModifiers.None);
            back.View.ContentRoot.panel.visualTree.SendEvent(tab);
            SendSubmit(back.View.ContentRoot.panel.visualTree);
            Assert.That(focus, Is.Zero);
            Assert.That(clicks, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Pointer_blocker_accepts_pointer_input_without_granting_control_focus () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter front = Create();
            UiToolkitViewAdapter blocker = Create();
            Button button = AddButton(front);
            Button blockedFocus = AddButton(blocker);
            await Apply(front);
            button.Focus();
            await host.ApplyAsync(new ViewPresentationChangeSet(new[]
            {
                new ViewPresentationChange(blocker, new ViewPresentation(true, true, 0, ViewInputMode.PointerOnly)),
            }));
            await UniTask.NextFrame();
            int pointers = 0;
            int focus = 0;
            blockedFocus.RegisterCallback<PointerDownEvent>(_ => pointers++, TrickleDown.TrickleDown);
            blockedFocus.RegisterCallback<FocusInEvent>(_ => focus++);
            using PointerDownEvent down = PointerDownEvent.GetPooled(new Event
            {
                type = EventType.MouseDown, mousePosition = new Vector2(10, 10), button = 0,
            });
            blockedFocus.SendEvent(down);
            blockedFocus.Focus();
            await UniTask.NextFrame();
            int clicks = 0;
            button.clicked += () => clicks++;
            SendSubmit(button);
            Assert.That(pointers, Is.EqualTo(1));
            Assert.That(focus, Is.Zero);
            Assert.That(blockedFocus.canGrabFocus, Is.False);
            Assert.That(blockedFocus.enabledSelf, Is.True);
            Assert.That(clicks, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Invalid_related_update_leaves_all_affected_input_closed () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter first = Create();
            UiToolkitViewAdapter second = Create();
            Button firstButton = AddButton(first);
            Button secondButton = AddButton(second);
            await Apply(first);
            Assert.That(first.View.TryCaptureInput(out UiInputSession old), Is.True);
            try
            {
                await host.ApplyAsync(new ViewPresentationChangeSet(new[]
                {
                    new ViewPresentationChange(first, new ViewPresentation(true, false, 0)),
                    new ViewPresentationChange(second, new ViewPresentation(true, true, int.MaxValue)),
                }));
                Assert.Fail("An unrepresentable drawing order must fail.");
            }
            catch (NavigationConfigurationException)
            {
            }
            Assert.That(old.IsValid, Is.False);
            Assert.That(firstButton.canGrabFocus || secondButton.canGrabFocus, Is.False);
            Assert.That(second.View.TryCaptureInput(out _), Is.False);
        });

        [UnityTest]
        public IEnumerator Foreground_standard_dropdown_keeps_native_selection_and_closing_behavior () => UniTask.ToCoroutine(async () =>
        {
            UiToolkitViewAdapter view = Create();
            DropdownField dropdown = new(new List<string> { "One", "Two" }, 0);
            view.View.ContentRoot.Add(dropdown);
            await Apply(view);
            dropdown.Focus();
            await UniTask.NextFrame();
#if UNITY_6000_0_OR_NEWER
            int dropdownFocus = 0;
            dropdown.RegisterCallback<FocusInEvent>(_ => dropdownFocus++);
#endif
            VisualElement? nativeFocus = null;
            dropdown.panel.visualTree.RegisterCallback<FocusEvent>(evt => nativeFocus = evt.target as VisualElement, TrickleDown.TrickleDown);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

            OpenDropdown(dropdown);
            await UniTask.NextFrame();
            ScrollView menu = dropdown.panel.visualTree.Q<ScrollView>(className: GenericDropdownMenu.containerInnerUssClassName);
            Assert.That(menu, Is.Not.Null);
            VisualElement content = menu.contentContainer;
            Assert.That(content, Is.Not.Null);
            Assert.That(content.canGrabFocus, Is.True);
            await UniTask.WaitUntil(() => ReferenceEquals(nativeFocus, content), cancellationToken: timeout.Token);
            using (KeyDownEvent end = KeyDownEvent.GetPooled('\0', KeyCode.End, EventModifiers.None))
            {
                end.target = content;
                content.SendEvent(end);
            }
            SendSubmit(content);
            await UniTask.NextFrame();
            Assert.That(dropdown.value, Is.EqualTo("Two"));
#if UNITY_6000_0_OR_NEWER
            // Unity 6 restores the control's focus. Older native menus only close.
            Assert.That(dropdownFocus, Is.GreaterThan(0));
#endif
            Assert.That(dropdown.panel.visualTree.Q(className: GenericDropdownMenu.ussClassName), Is.Null);

            OpenDropdown(dropdown);
            await UniTask.NextFrame();
            menu = dropdown.panel.visualTree.Q<ScrollView>(className: GenericDropdownMenu.containerInnerUssClassName);
            Assert.That(menu, Is.Not.Null);
            content = menu.contentContainer;
            Assert.That(content, Is.Not.Null);
            await UniTask.WaitUntil(() => ReferenceEquals(nativeFocus, content), cancellationToken: timeout.Token);
            using (NavigationCancelEvent cancel = NavigationCancelEvent.GetPooled())
            {
                content.SendEvent(cancel);
            }
            await UniTask.NextFrame();
            Assert.That(dropdown.value, Is.EqualTo("Two"));
            Assert.That(dropdown.panel.visualTree.Q(className: GenericDropdownMenu.ussClassName), Is.Null);
            Assert.That(dropdown.enabledSelf, Is.True);
        });

        [UnityTest]
        public IEnumerator Prefab_content_is_input_closed_during_application_awake_without_execution_order_rules () => UniTask.ToCoroutine(async () =>
        {
            GameObject prefab = Track(new GameObject("UI template"));
            prefab.SetActive(false);
            UiToolkitViewAdapter template = prefab.AddComponent<UiToolkitViewAdapter>();
            prefab.AddComponent<UiToolkitEarlyFocusProbe>();
            UiToolkitViewAdapter? acquired = null;
            RegionDefinitionId root = new("root");
            NavigationDefinition definition = NavigationDefinition.Build(root, RegionCompositionMode.Layered,
                region => region.AddRoute<Popup>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
            ScreenCatalog catalog = ScreenCatalog.Build(definition, builder => builder.RegisterScreens(root,
                screens => screens.RegisterScreen(new ScreenDefinition<Popup>(async (creation, token) =>
                {
                    acquired = await creation.Lifetime.AcquireUiToolkitViewAsync(template, host, token);
                    creation.ConnectPresentation(new ScreenPresentationBinding(new[] { acquired }));
                    return creation.Lifetime.CreateOwned(() => new Handler());
                }, ScreenInstancePolicy.Multiple))));
            NavigationHost runtime = NavigationHost.Create(catalog);
            navigation.Add(runtime);
            await runtime.StartAsync(new Popup());
            UiToolkitEarlyFocusProbe probe = acquired!.GetComponent<UiToolkitEarlyFocusProbe>();
            Assert.That(probe.QualifiedDuringAwake, Is.False);
            Assert.That(probe.FocusNotificationsDuringAwake, Is.Zero);
            Assert.That(acquired.IsAlive, Is.True);
            Assert.That(acquired.View.TryCaptureInput(out _), Is.True);
            await runtime.ShutdownAsync();
            Assert.That(acquired == null, Is.True);
        });

        private UiToolkitViewAdapter Create ()
        {
            GameObject value = Track(new GameObject("UI screen"));
            UiToolkitViewAdapter adapter = value.AddComponent<UiToolkitViewAdapter>();
            host.Connect(adapter);
            return adapter;
        }

        private static Button AddButton (UiToolkitViewAdapter adapter)
        {
            Button button = new();
            adapter.View.ContentRoot.Add(button);
            return button;
        }

        private ValueTask Apply (UiToolkitViewAdapter target, params UiToolkitViewAdapter[] background)
        {
            List<ViewPresentationChange> changes = new();
            foreach (UiToolkitViewAdapter view in background)
            {
                changes.Add(new ViewPresentationChange(view, new ViewPresentation(true, false, changes.Count)));
            }
            changes.Add(new ViewPresentationChange(target, new ViewPresentation(true, true, changes.Count)));
            return host.ApplyAsync(new ViewPresentationChangeSet(changes));
        }

        private ValueTask Close (UiToolkitViewAdapter view) => host.ApplyAsync(new ViewPresentationChangeSet(new[]
        {
            new ViewPresentationChange(view, new ViewPresentation(true, false, view.Presentation.Order)),
        }));

        private static void SendSubmit (VisualElement element)
        {
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            element.SendEvent(submit);
        }

        private static void OpenDropdown (DropdownField dropdown)
        {
#if UNITY_2023_2_OR_NEWER
            SendSubmit(dropdown);
#else
            using KeyDownEvent enter = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None);
            enter.target = dropdown;
            dropdown.SendEvent(enter);
#endif
        }

        private T Track<T> (T value) where T : Object
        {
            objects.Add(value);
            return value;
        }

        private sealed record Popup : Route;

        private sealed class Handler : IScreenLifecycleHandler<Popup>, IDisposable
        {
            public ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken) => default;

            public ValueTask PrepareAsync (Popup route, ScreenPreparationContext preparation, CancellationToken cancellationToken) => default;

            public ValueTask ActivateAsync (Popup route, ScreenActivityContext activity) => default;

            public ValueTask DeactivateAsync () => default;

            public ValueTask TerminateAsync (NavigationProgressReporter progress) => default;

            public void Dispose ()
            {
            }
        }
    }
}
