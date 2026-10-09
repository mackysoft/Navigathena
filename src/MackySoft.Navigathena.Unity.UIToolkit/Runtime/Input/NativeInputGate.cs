using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary> Rejects new operations within a closed view and ends its owned pointer captures. </summary>
    internal sealed class NativeInputGate : PointerManipulator
    {
        private readonly UiToolkitViewBinding binding;
        private readonly Dictionary<int, PointerMoveEvent> pointers = new();

        public NativeInputGate (UiToolkitViewBinding binding) => this.binding = binding;

        protected override void RegisterCallbacksOnTarget ()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnCancel, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureEvent>(OnCapture, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
            target.RegisterCallback<ClickEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.RegisterCallback<WheelEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseDownEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseUpEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseMoveEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.RegisterCallback<ContextClickEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyDownEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyUpEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationMoveEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationSubmitEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.RegisterCallback<NavigationCancelEvent>(OnFocusInput, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget ()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnCancel, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureEvent>(OnCapture, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
            target.UnregisterCallback<ClickEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<WheelEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseDownEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseUpEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseMoveEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<ContextClickEvent>(OnPointerInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<KeyDownEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<KeyUpEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<NavigationMoveEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<NavigationSubmitEvent>(OnFocusInput, TrickleDown.TrickleDown);
            target.UnregisterCallback<NavigationCancelEvent>(OnFocusInput, TrickleDown.TrickleDown);
            ClearPointers();
        }

        private void OnDown (PointerDownEvent evt)
        {
            if (!binding.InputAllowed)
            {
                Block(evt);
                return;
            }
            Remember(evt);
        }

        private void OnMove (PointerMoveEvent evt)
        {
            if (!binding.InputAllowed)
            {
                Block(evt);
                return;
            }
            if (pointers.ContainsKey(evt.pointerId))
            {
                Remember(evt);
            }
        }

        private void OnUp (PointerUpEvent evt)
        {
            Forget(evt.pointerId);
            OnPointerInput(evt);
        }

        private void OnCancel (PointerCancelEvent evt) => Forget(evt.pointerId);

        private void OnCapture (PointerCaptureEvent evt)
        {
            if (!binding.InputAllowed && binding.Panel.GetCapturingElement(evt.pointerId) is VisualElement captured
                && binding.OwnsNative(captured))
            {
                captured.ReleasePointer(evt.pointerId);
            }
        }

        private void OnCaptureOut (PointerCaptureOutEvent evt) => Forget(evt.pointerId);

        private void OnPointerInput (EventBase evt)
        {
            if (!binding.InputAllowed)
            {
                Block(evt);
            }
        }

        private void OnFocusInput (EventBase evt)
        {
            if (!binding.CanReceiveFocus)
            {
                Block(evt);
            }
        }

        private void Remember (IPointerEvent evt)
        {
            Forget(evt.pointerId);
            pointers.Add(evt.pointerId, PointerMoveEvent.GetPooled(evt));
        }

        private void Forget (int pointer)
        {
            if (pointers.TryGetValue(pointer, out PointerMoveEvent? snapshot))
            {
                pointers.Remove(pointer);
                snapshot.Dispose();
            }
        }

        public void CancelPointers ()
        {
            List<Exception>? failures = null;
            for (int pointer = 0; pointer < PointerId.maxPointers; pointer++)
            {
                if (binding.Panel.GetCapturingElement(pointer) is not VisualElement captured || !binding.OwnsNative(captured))
                {
                    continue;
                }
                try
                {
                    // End exclusive delivery first. Legacy PointerCancel also emits
                    // MouseUp, which must pass through the now-closed ancestor gate.
                    captured.ReleasePointer(pointer);
                    if (pointers.TryGetValue(pointer, out PointerMoveEvent? snapshot))
                    {
                        // Termination is delivered to the former capturer, not
                        // redistributed as input to another view.
                        using PointerCancelEvent cancel = PointerCancelEvent.GetPooled((IPointerEvent)snapshot);
                        cancel.target = captured;
                        captured.SendEvent(cancel);
                    }
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
                finally
                {
                    captured.ReleasePointer(pointer);
                }
            }
            ClearPointers();
            if (failures is not null)
            {
                throw new AggregateException("Owned pointer cancellation callbacks failed.", failures);
            }
        }

        private void ClearPointers ()
        {
            foreach (PointerMoveEvent snapshot in pointers.Values)
            {
                snapshot.Dispose();
            }
            pointers.Clear();
        }

        private void Block (EventBase evt)
        {
#if UNITY_2023_2_OR_NEWER
            binding.Panel.focusController.IgnoreEvent(evt);
#endif
            evt.StopImmediatePropagation();
#if !UNITY_6000_0_OR_NEWER
            // Older UI Toolkit runs target default actions after propagation stops.
            evt.PreventDefault();
#endif
        }
    }
}
