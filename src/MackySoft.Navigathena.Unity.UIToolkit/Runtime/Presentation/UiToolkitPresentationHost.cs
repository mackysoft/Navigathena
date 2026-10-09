using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary> Owns native view connections and applies navigation's presentation permissions. </summary>
    /// <remarks> Create and use on the Unity player thread. Release connected views before disposing the host. The settings template remains externally owned. </remarks>
    public sealed class UiToolkitPresentationHost : IViewPresentationBatch, IDisposable
    {
        private readonly PanelSettings template;
        private readonly HashSet<UiToolkitViewBinding> bindings = new();
        private readonly SemaphoreSlim updates = new(1, 1);
        private GameObject? staging;
        private bool disposed;

        public UiToolkitPresentationHost (PanelSettings settingsTemplate, int baseOrder = 0)
        {
            UnityThread.AssertCurrent();
            template = settingsTemplate != null ? settingsTemplate : throw new ArgumentNullException(nameof(settingsTemplate));
            if (template.targetTexture != null)
            {
                throw new NavigationConfigurationException("A presentation host requires display-panel settings.");
            }
            BaseOrder = baseOrder;
        }

        internal int BaseOrder { get; }

        /// <summary> Connects an acquired view to a new, input-closed native panel. </summary>
        /// <remarks> Configure UXML on the adapter. The host privately owns its document and panel; screen input permissions belong to navigation. </remarks>
        public IUiToolkitView Connect (UiToolkitViewAdapter adapter)
        {
            UnityThread.AssertCurrent();
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }
            if (!adapter.isActiveAndEnabled)
            {
                throw new NavigationConfigurationException("Connect an acquired, active adapter; use the prefab acquisition for inactive templates.");
            }
            PrepareConnection(adapter);
            try
            {
                AttachConnection(adapter);
                return adapter.View;
            }
            catch
            {
                adapter.Binding?.Dispose();
                throw;
            }
        }

        internal void PrepareConnection (UiToolkitViewAdapter adapter)
        {
            UnityThread.AssertCurrent();
            EnsureOpen();
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }
            if (adapter.HasBinding)
            {
                throw new NavigationConfigurationException("The view is already connected to a presentation host.");
            }
            if (adapter.GetComponentInChildren<UIDocument>(true) != null)
            {
                throw new NavigationConfigurationException("Do not place UIDocument on a managed screen prefab. Configure content on UiToolkitViewAdapter; the host owns its native document.");
            }
            PanelSettings settings = UnityEngine.Object.Instantiate(template);
            GameObject native = new("Navigathena UI Toolkit document") { hideFlags = HideFlags.HideInHierarchy };
            native.SetActive(false);
            native.transform.SetParent(Staging, false);
            UiToolkitViewBinding? binding = null;
            try
            {
                UIDocument document = native.AddComponent<UIDocument>();
                document.panelSettings = settings;
                binding = new UiToolkitViewBinding(adapter, this, document, settings, adapter.ContentAsset);
                bindings.Add(binding);
                adapter.Bind(binding);
            }
            catch
            {
                if (binding is not null)
                {
                    binding.Dispose();
                }
                else
                {
                    UnityEngine.Object.Destroy(native);
                    UnityEngine.Object.Destroy(settings);
                }
                throw;
            }
        }

        internal void AttachConnection (UiToolkitViewAdapter adapter)
        {
            UnityThread.AssertCurrent();
            UiToolkitViewBinding binding = RequireBinding(adapter);
            if (!adapter.isActiveAndEnabled || binding.IsAttached)
            {
                throw new NavigationConfigurationException("Native attachment requires an active, prepared view and can run only once.");
            }
            binding.Attach();
        }

        internal Transform Staging
        {
            get
            {
                EnsureOpen();
                if (staging == null)
                {
                    staging = new GameObject("Navigathena UI Toolkit acquisition");
                    staging.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(staging);
                }
                return staging.transform;
            }
        }

        internal void Remove (UiToolkitViewBinding binding) => bindings.Remove(binding);

        internal void ApplySynchronous (UiToolkitViewBinding binding, ViewPresentation presentation)
        {
            binding.Validate(presentation);
            bool changingInput = ChangesInput(binding, presentation);
            if (changingInput && (presentation.InputMode != ViewInputMode.PointerOnly
                || binding.InputAllowed && binding.Presentation.InputMode == ViewInputMode.All))
            {
                throw new NavigationConfigurationException("Apply screen permission changes through the presentation batch, not IViewAdapter.Apply.");
            }
            try
            {
                if (changingInput)
                {
                    RevokeAndClose(new[] { binding });
                }
                binding.ApplyVisual(presentation);
                if (AllowsInput(presentation))
                {
                    binding.OpenInput();
                }
            }
            catch
            {
                RevokeAndClose(new[] { binding });
                throw;
            }
        }

        /// <summary> Applies the supplied screen permissions after previously admitted native events have settled. </summary>
        /// <remarks> A failed update closes every affected input scope. This method does not select an input recipient or redistribute events. </remarks>
        public async ValueTask ApplyAsync (ViewPresentationChangeSet changes)
        {
            UnityThread.AssertCurrent();
            EnsureOpen();
            if (changes is null)
            {
                throw new ArgumentNullException(nameof(changes));
            }
            await updates.WaitAsync();
            UiToolkitViewBinding[] affected = Array.Empty<UiToolkitViewBinding>();
            try
            {
                affected = changes.Changes.Select(change => (change.View as UiToolkitViewAdapter)?.Binding)
                    .Where(binding => binding is not null && bindings.Contains(binding)).Cast<UiToolkitViewBinding>().Distinct().ToArray();
                if (affected.Length != changes.Changes.Count)
                {
                    throw new NavigationConfigurationException("The change set contains a view outside this presentation host.");
                }
                bool permissionChanged = changes.Changes.Any(change => ChangesInput(RequireBinding(change.View), change.Presentation));
                if (permissionChanged)
                {
                    // Native focus events are admitted before dispatch. Finish that
                    // permission period before revoking it, not inside an event callback.
                    await SettleAsync(affected);
                }
                foreach (ViewPresentationChange change in changes.Changes)
                {
                    RequireBinding(change.View).Validate(change.Presentation);
                }
                if (permissionChanged)
                {
                    UiToolkitViewBinding[] closing = changes.Changes
                        .Where(change => ChangesInput(RequireBinding(change.View), change.Presentation))
                        .Select(change => RequireBinding(change.View)).ToArray();
                    RevokeAndClose(closing);
                }
                foreach (ViewPresentationChange change in changes.Changes)
                {
                    UiToolkitViewBinding binding = RequireBinding(change.View);
                    binding.ApplyVisual(change.Presentation);
                    if (AllowsInput(change.Presentation) && binding.Grant is null)
                    {
                        binding.OpenInput();
                    }
                }
                if (permissionChanged)
                {
                    await SettleAsync(affected);
                }
            }
            catch (Exception failure)
            {
                List<Exception> failures = new() { failure };
                try
                {
                    await SettleAsync(affected);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                try
                {
                    RevokeAndClose(affected);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                if (failures.Count > 1)
                {
                    throw new AggregateException("The presentation update failed; affected input remains closed.", failures);
                }
                throw;
            }
            finally
            {
                updates.Release();
            }
        }

        private static Task SettleAsync (IEnumerable<UiToolkitViewBinding> views)
            => Task.WhenAll(views.Where(binding => binding.IsAlive).Select(binding => binding.SettleDispatchAsync().AsTask()));

        private static void RevokeAndClose (IReadOnlyList<UiToolkitViewBinding> views)
        {
            // Invalidate all scopes before invoking any application cleanup callback.
            List<ViewInputGrant> revoked = new();
            foreach (UiToolkitViewBinding binding in views)
            {
                if (binding.RevokeInput() is ViewInputGrant grant)
                {
                    revoked.Add(grant);
                }
            }
            List<Exception> failures = new();
            foreach (ViewInputGrant grant in revoked)
            {
                try
                {
                    grant.NotifyRevocation();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            foreach (UiToolkitViewBinding binding in views)
            {
                try
                {
                    binding.CloseNativeOperations();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("Native input cleanup failed.", failures);
            }
        }

        private UiToolkitViewBinding RequireBinding (IViewAdapter adapter)
        {
            if (adapter is not UiToolkitViewAdapter view || view.Binding is not UiToolkitViewBinding binding || !bindings.Contains(binding))
            {
                throw new NavigationConfigurationException("The change set contains a view outside this presentation host.");
            }
            return binding;
        }

        private static bool AllowsInput (ViewPresentation value) => value.OutputEnabled && value.InputEnabled;

        private static bool ChangesInput (UiToolkitViewBinding binding, ViewPresentation value)
            => binding.InputAllowed != AllowsInput(value) || binding.Grant is not null && binding.Presentation.InputMode != value.InputMode;

        private void EnsureOpen ()
        {
            if (disposed || template == null)
            {
                throw new ObjectDisposedException(nameof(UiToolkitPresentationHost));
            }
        }

        public void Dispose ()
        {
            UnityThread.AssertCurrent();
            if (disposed)
            {
                return;
            }
            if (bindings.Count != 0)
            {
                throw new InvalidOperationException("Release all connected views before disposing their presentation host.");
            }
            disposed = true;
            if (staging != null)
            {
                UnityEngine.Object.Destroy(staging);
            }
            updates.Dispose();
        }
    }
}
