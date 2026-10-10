using MackySoft.Navigathena.Unity.UIToolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.Tests
{
    public sealed class UiToolkitEarlyFocusProbe : MonoBehaviour
    {
        public bool QualifiedDuringAwake { get; private set; }
        public int FocusNotificationsDuringAwake { get; private set; }
        private void Awake ()
        {
            IUiToolkitView view = GetComponent<UiToolkitViewAdapter>().View;
            Button button = new();
            view.ContentRoot.Add(button);
            button.RegisterCallback<FocusInEvent>(_ => FocusNotificationsDuringAwake++);
            QualifiedDuringAwake = button.canGrabFocus;
            button.Focus();
        }
    }
}
