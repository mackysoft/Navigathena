using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    internal sealed class ViewInputBoundary : PointerManipulator
    {
        public bool Enabled
        {
            get; set;
        }

        protected override void RegisterCallbacksOnTarget ()
        {
            target.RegisterCallback<PointerDownEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<ClickEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<WheelEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyDownEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyUpEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationMoveEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationSubmitEvent>(Stop, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationCancelEvent>(Stop, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget ()
        {
            target.UnregisterCallback<PointerDownEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<ClickEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<WheelEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<KeyDownEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<KeyUpEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<NavigationMoveEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<NavigationSubmitEvent>(Stop, TrickleDown.TrickleDown);
            target.UnregisterCallback<NavigationCancelEvent>(Stop, TrickleDown.TrickleDown);
        }

        private void Stop (EventBase evt)
        {
            if (!Enabled)
            {
                evt.StopImmediatePropagation();
            }
        }
    }
}
