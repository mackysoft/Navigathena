using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MackySoft.Navigathena.Unity.NativeResources
{
    internal sealed class ComponentObservation<T> : IResourceAcquisition<T> where T : Component
    {
        private readonly T component;
        private NativeResourceMonitor? monitor;

        public ComponentObservation (T component) => this.component = component;

        public ValueTask<T> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            UnityThread.AssertCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            if (component == null)
            {
                throw new InvalidOperationException("The acquired component was destroyed before it could be observed.");
            }

            monitor = NativeResourceMonitor.Observe(component, context);
            return new ValueTask<T>(component);
        }

        public ValueTask DisposeAsync ()
        {
            UnityThread.AssertCurrent();
            if (monitor != null)
            {
                monitor.Stop();
                monitor = null;
            }

            return default;
        }
    }
}
