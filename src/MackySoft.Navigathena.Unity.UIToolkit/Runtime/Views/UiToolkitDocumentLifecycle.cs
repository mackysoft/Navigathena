#if UNITY_EDITOR
using UnityEngine;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary> Preserves the managed connection across the UIDocument companion reload callbacks. </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed class UiToolkitDocumentLifecycle : MonoBehaviour
    {
        private UiToolkitViewBinding? binding;

        internal void Bind (UiToolkitViewBinding value) => binding = value;

        // UIDocument disables and enables companions around Editor live reload.
        // The public screen adapter's disable still ends the screen connection.
        private void OnDisable () => binding?.BeginDocumentReload();

        private void OnEnable () => binding?.CompleteDocumentReload();

        private void OnDestroy () => binding?.ReportLoss();
    }
}
#endif
