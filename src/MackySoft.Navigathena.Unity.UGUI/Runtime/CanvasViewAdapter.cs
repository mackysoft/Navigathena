using System;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MackySoft.Navigathena.Unity.UGUI
{
    /// <summary>Controls an independently sorted Canvas and its raycaster without changing Selectable.interactable.</summary>
    /// <remarks>Own the Canvas and GraphicRaycaster exclusively. Game input subscriptions belong to screen activity. Custom input modules must not force selection into a closed screen.</remarks>
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    [DefaultExecutionOrder(-32000)]
    public sealed class CanvasViewAdapter : MonoBehaviour, IViewAdapter
    {
        [SerializeField] private bool presentBeforeNavigation;
        [SerializeField] private int baseSortingOrder;
        private Canvas? canvas;
        private GraphicRaycaster? raycaster;
        private bool lost;

        public event Action<string>? Lost;
        public object Identity => Canvas.GetEntityId();
        public object OrderingDomain => (Canvas.renderMode, Canvas.targetDisplay, Canvas.sortingLayerID, Canvas.worldCamera != null ? Canvas.worldCamera.GetEntityId() : default, baseSortingOrder);
        public bool IsAlive => this != null && canvas != null && raycaster != null && enabled && gameObject.activeInHierarchy;
        public ViewPresentation Presentation
        {
            get; private set;
        }
        private Canvas Canvas => canvas != null ? canvas : canvas = GetComponent<Canvas>();

        private void Awake ()
        {
            raycaster = GetComponent<GraphicRaycaster>();
            Apply(new ViewPresentation(presentBeforeNavigation, false, checked(Canvas.sortingOrder - baseSortingOrder)));
        }

        public void Validate (ViewPresentation presentation)
        {
            UnityThread.AssertCurrent();
            canvas = Canvas;
            if (raycaster == null)
            {
                raycaster = GetComponent<GraphicRaycaster>();
            }

            if (!IsAlive)
            {
                throw new InvalidOperationException("The Canvas view is unavailable.");
            }

            if (canvas.renderMode == RenderMode.WorldSpace || !canvas.isRootCanvas)
            {
                throw new NavigationConfigurationException("This adapter requires an independent screen-space root Canvas.");
            }

            int order = checked(baseSortingOrder + presentation.Order);
            if (order < short.MinValue || order > short.MaxValue)
            {
                throw new NavigationConfigurationException("The Canvas sorting order is outside its supported range.");
            }
        }

        public void Apply (ViewPresentation presentation)
        {
            Validate(presentation);

            canvas!.sortingOrder = checked(baseSortingOrder + presentation.Order);
            raycaster!.enabled = presentation.OutputEnabled && presentation.InputEnabled;
            canvas.enabled = presentation.OutputEnabled;
            Presentation = presentation;
            ClearClosedSelection();
        }

        private void Update ()
        {
            if (!IsAlive)
            {
                ReportLoss();
                return;
            }

            ClearClosedSelection();
        }

        private void ClearClosedSelection ()
        {
            EventSystem system = EventSystem.current;
            GameObject? selected = system != null ? system.currentSelectedGameObject : null;
            if (!Presentation.InputEnabled && system != null && selected != null && selected.transform.IsChildOf(transform))
            {
                system.SetSelectedGameObject(null!);
            }
        }

        private void OnDisable () => ReportLoss();
        private void OnDestroy () => ReportLoss();
        private void ReportLoss ()
        {
            if (lost || Lost is null)
            {
                return;
            }

            lost = true;
            Lost.Invoke("The registered Canvas or its input boundary is no longer available.");
        }
    }
}
