using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary> Provides managed screen content and read-only input permission periods. </summary>
    public interface IUiToolkitView
    {
        /// <summary> The registered content boundary. Application controls remain within this root. </summary>
        VisualElement ContentRoot { get; }

        /// <summary> Captures this view's current input permission period on the Unity player thread. </summary>
        /// <returns> False when the view's input permission is closed or its connection has ended. </returns>
        bool TryCaptureInput (out UiInputSession session);
    }
}
