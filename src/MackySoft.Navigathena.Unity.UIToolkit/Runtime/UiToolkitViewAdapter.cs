using System;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Connects an acquired UI Toolkit screen to its presentation host.</summary>
    /// <remarks>Assign UXML to ContentAsset. The host owns the native document and panel; presentation permissions belong to the navigation runtime.</remarks>
    [DisallowMultipleComponent]
    public sealed class UiToolkitViewAdapter : MonoBehaviour, IViewPresentationBatchAdapter
    {
        [SerializeField] private VisualTreeAsset? contentAsset;
        private UiToolkitViewBinding? binding;
        private bool lost;

        public event Action<string>? Lost;

        /// <summary>The borrowed UXML used to construct this view's content before native attachment.</summary>
        public VisualTreeAsset? ContentAsset
        {
            get => contentAsset;
            set
            {
                if (binding is not null)
                {
                    throw new InvalidOperationException("Content must be configured before the view is connected.");
                }
                contentAsset = value;
            }
        }

        /// <summary>The screen implementation's content and input-session interface.</summary>
        public IUiToolkitView View => RequireBinding();
        internal UiToolkitViewBinding? Binding => binding;
        internal bool HasBinding => binding is not null;
        public object Identity => RequireBinding().Identity;
        public object OrderingDomain => RequireBinding().Host;
        public bool IsAlive => binding?.IsAlive == true;
        public ViewPresentation Presentation => binding?.Presentation ?? default;
        IViewPresentationBatch IViewPresentationBatchAdapter.PresentationBatch => RequireBinding().Host;

        internal void Bind (UiToolkitViewBinding value) => binding = value;

        public void Validate (ViewPresentation presentation) => RequireBinding().Validate(presentation);

        void IViewAdapter.Apply (ViewPresentation presentation)
        {
            UnityThread.AssertCurrent();
            UiToolkitViewBinding current = RequireBinding();
            current.Host.ApplySynchronous(current, presentation);
        }

        private void Update ()
        {
            if (binding is not null)
            {
                if (!binding.IsAlive)
                {
                    binding.ReportLoss();
                }
            }
        }

        private void OnDisable () => binding?.ReportLoss();

        private void OnDestroy () => binding?.Dispose();

        internal void ReportLoss ()
        {
            if (lost)
            {
                return;
            }
            lost = true;
            Lost?.Invoke("The registered UI Toolkit document or native input scope is no longer available.");
        }

        private UiToolkitViewBinding RequireBinding () => binding
            ?? throw new NavigationConfigurationException("Connect the view to UiToolkitPresentationHost before registering its presentation.");
    }
}
