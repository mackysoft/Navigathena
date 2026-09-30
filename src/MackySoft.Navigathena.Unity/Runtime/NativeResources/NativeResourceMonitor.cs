using UnityEngine;

namespace MackySoft.Navigathena.Unity.NativeResources
{
    /// <summary>Observes a native object independently of that object's active state and scene.</summary>
    internal sealed class NativeResourceMonitor : MonoBehaviour
    {
        private Object? target;
        private ResourceAcquisitionContext? context;

        internal static NativeResourceMonitor Observe (Object target, ResourceAcquisitionContext context)
        {
            GameObject observer = new("Navigathena native resource observation");
            DontDestroyOnLoad(observer);
            NativeResourceMonitor monitor = observer.AddComponent<NativeResourceMonitor>();
            monitor.target = target;
            monitor.context = context;
            return monitor;
        }

        private void Update ()
        {
            if (context is not null && target == null)
            {
                ResourceAcquisitionContext report = context;
                context = null;
                report.ReportLoss("The acquired Unity object was destroyed outside its managed lifetime.");
                Destroy(gameObject);
            }
        }

        internal void Stop ()
        {
            context = null;
            Destroy(gameObject);
        }

        private void OnDestroy ()
        {
            if (context is not null)
            {
                ResourceAcquisitionContext report = context;
                context = null;
                report.ReportLoss("The Unity resource observation was destroyed before its owner ended.");
            }
        }
    }
}
