using System;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Unity.NativeResources;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    internal sealed class UiToolkitPrefabAcquisition : IResourceAcquisition<UiToolkitViewAdapter>
    {
        private readonly UiToolkitViewAdapter prefab;
        private readonly UiToolkitPresentationHost host;
        private GameObject? instance;
        private NativeResourceMonitor? monitor;

        public UiToolkitPrefabAcquisition (UiToolkitViewAdapter prefab, UiToolkitPresentationHost host)
        {
            this.prefab = prefab;
            this.host = host;
        }
        public ValueTask<UiToolkitViewAdapter> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }
            if (prefab.transform.parent != null)
            {
                throw new NavigationConfigurationException("The UI Toolkit prefab reference must identify its root adapter.");
            }
            instance = UnityEngine.Object.Instantiate(prefab.gameObject, host.Staging);
            UiToolkitViewAdapter[] views = instance.GetComponentsInChildren<UiToolkitViewAdapter>(true);
            if (views.Length != 1 || views[0].gameObject != instance)
            {
                throw new NavigationConfigurationException("The prefab must contain exactly one UI Toolkit adapter on its root.");
            }
            host.PrepareConnection(views[0]);
            instance.SetActive(true);
            instance.transform.SetParent(null, false);
            UnityEngine.Object.DontDestroyOnLoad(instance);
            host.AttachConnection(views[0]);
            monitor = NativeResourceMonitor.Observe(instance, context);
            if (!views[0].IsAlive)
            {
                throw new NavigationConfigurationException("The acquired UI Toolkit view did not attach to its managed panel.");
            }
            return new ValueTask<UiToolkitViewAdapter>(views[0]);
        }
        public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
        {
            UnityThread.AssertCurrent();
            if (monitor != null)
            {
                monitor.Stop();
                monitor = null;
            }
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
                while (instance != null)
                {
                    await UnityFrame.WaitNextAsync(CancellationToken.None);
                }
            }
        }
    }
}
