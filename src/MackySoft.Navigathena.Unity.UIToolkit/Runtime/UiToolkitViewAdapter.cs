using System;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Connects a UIDocument to runtime visibility, ordering, and event interception without disabled styling.</summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public sealed class UiToolkitViewAdapter : MonoBehaviour, IViewAdapter
    {
        private const string HiddenClass = "navigathena-view-output-closed";
        [SerializeField] private bool presentBeforeNavigation;
        [SerializeField] private int baseSortingOrder;
        private UIDocument? document;
        private VisualElement? root;
        private ViewInputBoundary? input;
        private bool lost;
        private VisualElement? closedReplacement;

        public event Action<string>? Lost;
        public object Identity => Document.GetEntityId();
        public object OrderingDomain => Document.panelSettings != null
            ? (Document.panelSettings.GetEntityId(), baseSortingOrder)
            : throw new NavigationConfigurationException("A UIDocument requires PanelSettings.");
        public bool IsAlive => this != null && document != null && enabled && gameObject.activeInHierarchy && root is not null && ReferenceEquals(root, document.rootVisualElement);
        public ViewPresentation Presentation
        {
            get; private set;
        }
        private UIDocument Document => document != null ? document : document = GetComponent<UIDocument>();

        private void Awake ()
        {
            Initialize();
            Apply(new ViewPresentation(presentBeforeNavigation, false, checked((int)Document.sortingOrder - baseSortingOrder)));
        }

        private void Initialize ()
        {
            if (root is not null)
            {
                return;
            }

            root = Document.rootVisualElement ?? throw new NavigationConfigurationException("The UIDocument has no initialized root.");
            StyleSheet permissions = Resources.Load<StyleSheet>("NavigathenaViewPermissions")
                ?? throw new NavigationConfigurationException("The UI Toolkit adapter's permission stylesheet is missing.");
            root.styleSheets.Add(permissions);
            input = new ViewInputBoundary();
            root.AddManipulator(input);
        }

        public void Validate (ViewPresentation presentation)
        {
            UnityThread.AssertCurrent();
            if (!IsAlive)
            {
                throw new InvalidOperationException("The UIDocument or its registered visual tree was replaced or destroyed.");
            }

            if (Document.panelSettings == null || Document.panelSettings.targetTexture != null)
            {
                throw new NavigationConfigurationException("This adapter requires a display panel, not a render-texture panel.");
            }
            if (transform.parent != null && transform.parent.GetComponentInParent<UIDocument>(true) != null)
            {
                throw new NavigationConfigurationException("A managed UIDocument must be a top-level document in its panel.");
            }

            int order = checked(baseSortingOrder + presentation.Order);
            if (order < -16777216 || order > 16777216)
            {
                throw new NavigationConfigurationException("The UIDocument sorting order must be exactly representable as a float.");
            }
        }

        public void Apply (ViewPresentation presentation)
        {
            UnityThread.AssertCurrent();
            Initialize();
            Validate(presentation);

            Document.sortingOrder = checked(baseSortingOrder + presentation.Order);
            input!.Enabled = presentation.InputEnabled && presentation.OutputEnabled;
            root!.EnableInClassList(HiddenClass, !presentation.OutputEnabled);
            if (!input.Enabled && root.panel is IPanel panel)
            {
                for (int pointer = 0; pointer < PointerId.maxPointers; pointer++)
                {
                    if (panel.GetCapturingElement(pointer) is VisualElement captured && (ReferenceEquals(captured, root) || root.Contains(captured)))
                    {
                        captured.ReleasePointer(pointer);
                    }
                }
            }
            if (!input.Enabled && root.panel?.focusController.focusedElement is VisualElement focused && root.Contains(focused))
            {
                focused.Blur();
            }

            Presentation = presentation;
        }

        private void Update ()
        {
            if (!IsAlive)
            {
                CloseReplacementTree();
                ReportLoss();
            }
        }

        private void CloseReplacementTree ()
        {
            VisualElement? replacement = document != null ? document.rootVisualElement : null;
            if (replacement is null || ReferenceEquals(replacement, root) || ReferenceEquals(replacement, closedReplacement))
            {
                return;
            }

            closedReplacement = replacement;
            replacement.AddManipulator(new ViewInputBoundary());
        }

        private void OnDisable () => ReportLoss();
        private void OnDestroy ()
        {
            ReportLoss();
            if (root is not null && input is not null)
            {
                root.RemoveManipulator(input);
            }
        }

        private void ReportLoss ()
        {
            if (lost || Lost is null)
            {
                return;
            }

            lost = true;
            Lost.Invoke("The registered UIDocument or visual tree is no longer available.");
        }
    }
}
