using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.NativeResources
{
    internal sealed class PrefabInstantiation<T> : IResourceAcquisition<T> where T : Component
    {
        private readonly GameObject prefab;
        private GameObject? instance;
        private NativeResourceMonitor? monitor;

        public PrefabInstantiation (GameObject prefab) => this.prefab = prefab;

        public ValueTask<T> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            instance = UnityEngine.Object.Instantiate(prefab);
            // A managed instance must not accidentally inherit the currently active scene's lifetime.
            UnityEngine.Object.DontDestroyOnLoad(instance);
            monitor = NativeResourceMonitor.Observe(instance, context);
            T[] views = instance.GetComponentsInChildren<T>(true);
            if (views.Length != 1)
            {
                throw new NavigationConfigurationException("The prefab must contain exactly one " + typeof(T).FullName + ".");
            }

            return new ValueTask<T>(views[0]);
        }

        public async ValueTask DisposeAsync ()
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
                // Native destruction is deferred. Do not release the source asset before it finishes.
                while (instance != null)
                {
                    await Awaitable.NextFrameAsync();
                }
            }
        }
    }
}
