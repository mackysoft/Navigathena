using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Owns one native connection. A lost connection is never revived.</summary>
    internal sealed class UiToolkitViewBinding : IUiToolkitView
    {
        private readonly UiToolkitViewAdapter adapter;
        private readonly UIDocument document;
        private readonly PanelSettings settings;
        private readonly NativeFocusBoundary boundary;
        private readonly HashSet<TaskCompletionSource<object?>> checkpoints = new();
        private VisualElement? documentRoot;
        private IPanel? panel;
        private NativeInputGate? input;
        private bool lost;
        private bool attached;
        private bool disposed;

        public UiToolkitViewBinding (UiToolkitViewAdapter adapter, UiToolkitPresentationHost host, UIDocument document, PanelSettings settings, VisualTreeAsset? source)
        {
            this.adapter = adapter;
            this.document = document;
            this.settings = settings;
            Host = host;
            ContentRoot = new VisualElement { name = "navigathena-content", pickingMode = PickingMode.Ignore };
            ContentRoot.AddToClassList("navigathena-view-content");
            ContentRoot.style.display = DisplayStyle.None;
            boundary = new NativeFocusBoundary(this, new VisualElement());
            boundary.Add(ContentRoot);
            if (source != null)
            {
                source.CloneTree(ContentRoot);
            }
        }

        public UiToolkitPresentationHost Host { get; }
        internal ViewInputGrant? Grant { get; private set; }
        internal bool InputAllowed => IsAlive && Grant is not null && Presentation.OutputEnabled && Presentation.InputEnabled;
        internal bool CanReceiveFocus => InputAllowed && Presentation.InputMode == ViewInputMode.All;
        public VisualElement ContentRoot { get; }
        public IViewAdapter Adapter => adapter;
        public object Identity => UnityObjectIdentity.Get(document);
        public ViewPresentation Presentation { get; private set; }
        public bool IsAttached => attached;
        public IPanel Panel => panel ?? throw new InvalidOperationException("The view has no native panel.");
        public VisualElement NativeRoot => documentRoot ?? throw new InvalidOperationException("The view has no native root.");
        public bool IsAlive => !lost && attached && adapter != null && adapter.enabled && adapter.gameObject.activeInHierarchy
            && document != null && document.isActiveAndEnabled && settings != null
            && ReferenceEquals(document.rootVisualElement, documentRoot) && ReferenceEquals(document.panelSettings, settings)
            && documentRoot is not null && ReferenceEquals(documentRoot.hierarchy.parent, boundary.contentContainer)
            && ReferenceEquals(boundary.panel, panel);

        public void Attach ()
        {
            UnityThread.AssertCurrent();
            document.gameObject.transform.SetParent(null, false);
            UnityEngine.Object.DontDestroyOnLoad(document.gameObject);
            document.gameObject.SetActive(true);
            documentRoot = document.rootVisualElement ?? throw new NavigationConfigurationException("The managed UIDocument did not create a root.");
            panel = documentRoot.panel ?? throw new NavigationConfigurationException("The managed UIDocument did not create a panel.");
            StyleSheet sheet = Resources.Load<StyleSheet>("NavigathenaViewVisibility")
                ?? throw new NavigationConfigurationException("The UI Toolkit presentation stylesheet is missing.");
            boundary.styleSheets.Add(sheet);
            boundary.style.display = DisplayStyle.None;

            // Attach only the registered view's document to its input boundary.
            documentRoot.RemoveFromHierarchy();
            panel.visualTree.Add(boundary);
            boundary.Add(documentRoot);
            documentRoot.Add(ContentRoot);
            input = new NativeInputGate(this);
            // Composite roots run target callbacks after descendant targets in older
            // UI Toolkit. Intercept on their ordinary content container instead.
            boundary.contentContainer.AddManipulator(input);
            documentRoot.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            attached = true;
        }

        public void Validate (ViewPresentation presentation)
        {
            UnityThread.AssertCurrent();
            if (!IsAlive)
            {
                throw new InvalidOperationException("The managed UI Toolkit connection has ended or changed.");
            }
            int order = checked(Host.BaseOrder + presentation.Order);
            if (order < -16777216 || order > 16777216)
            {
                throw new NavigationConfigurationException("The panel order must be exactly representable as a float.");
            }
        }

        public void ApplyVisual (ViewPresentation presentation)
        {
            settings.sortingOrder = checked(Host.BaseOrder + presentation.Order);
            // The host owns this root. Its output state must take effect before native
            // focus admission; stylesheet invalidation would defer that state change.
            boundary.style.display = presentation.OutputEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            ContentRoot.style.display = presentation.OutputEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            Presentation = presentation;
        }

        public void CloseNativeOperations ()
        {
            try
            {
                input?.CancelPointers();
            }
            finally
            {
                Blur();
            }
        }

        public void Blur ()
        {
            if (panel?.focusController.focusedElement is VisualElement focused && OwnsNative(focused))
            {
                focused.Blur();
            }
        }

        internal bool OwnsNative (VisualElement target) => ReferenceEquals(target.panel, panel)
            && (ReferenceEquals(target, boundary) || boundary.Contains(target));

        internal ViewInputGrant? RevokeInput ()
        {
            ViewInputGrant? previous = Grant;
            Grant = null;
            previous?.Invalidate();
            return previous;
        }

        internal void OpenInput () => Grant ??= new ViewInputGrant(this);

        public bool TryCaptureInput (out UiInputSession session)
        {
            UnityThread.AssertCurrent();
            if (InputAllowed && Grant is ViewInputGrant grant)
            {
                session = new UiInputSession(grant);
                return true;
            }
            session = default;
            return false;
        }

        public async ValueTask SettleDispatchAsync ()
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("The native dispatch connection has ended.");
            }
            TaskCompletionSource<object?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            checkpoints.Add(completion);
            try
            {
                using NativeDispatchCheckpoint checkpoint = NativeDispatchCheckpoint.GetPooled();
                checkpoint.Completion = completion;
                checkpoint.target = NativeRoot;
                NativeRoot.SendEvent(checkpoint);
                await completion.Task;
                UnityThread.AssertCurrent();
            }
            finally
            {
                checkpoints.Remove(completion);
            }
        }

        public void ReportLoss ()
        {
            if (lost)
            {
                return;
            }
            lost = true;
            ViewInputGrant? grant = RevokeInput();
            foreach (TaskCompletionSource<object?> checkpoint in checkpoints)
            {
                checkpoint.TrySetException(new InvalidOperationException("The view ended before native dispatch settled."));
            }
            try
            {
                grant?.NotifyRevocation();
            }
            finally
            {
                try
                {
                    CloseNativeOperations();
                }
                finally
                {
                    adapter.ReportLoss();
                }
            }
        }

        public void Dispose ()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                ReportLoss();
            }
            finally
            {
                if (documentRoot is not null)
                {
                    documentRoot.UnregisterCallback<DetachFromPanelEvent>(OnDetached);
                }
                if (input is not null)
                {
                    boundary.contentContainer.RemoveManipulator(input);
                }
                boundary.RemoveFromHierarchy();
                Host.Remove(this);
                if (document != null)
                {
                    UnityEngine.Object.Destroy(document.gameObject);
                }
                if (settings != null)
                {
                    UnityEngine.Object.Destroy(settings);
                }
            }
        }

        private void OnDetached (DetachFromPanelEvent evt)
        {
            if (ReferenceEquals(evt.target, documentRoot))
            {
                ReportLoss();
            }
        }
    }
}
