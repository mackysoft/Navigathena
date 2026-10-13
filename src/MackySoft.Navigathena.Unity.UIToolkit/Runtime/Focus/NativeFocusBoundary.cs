using UnityEngine.UIElements;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Uses a native composite root to constrain existing and future descendants without disabled styling.</summary>
    internal sealed class NativeFocusBoundary : BaseField<bool>
    {
        private readonly VisualElement content;
        private readonly UiToolkitViewBinding viewBinding;

        public NativeFocusBoundary (UiToolkitViewBinding binding, VisualElement content) : base(null, content)
        {
            viewBinding = binding;
            this.content = content;
            RemoveFromClassList(ussClassName);
            content.RemoveFromClassList(inputUssClassName);
            content.focusable = false;
            content.pickingMode = PickingMode.Ignore;
            AddToClassList("navigathena-input-scope");
            content.AddToClassList("navigathena-input-scope-content");
            labelElement.RemoveFromHierarchy();
            pickingMode = PickingMode.Ignore;
        }

        // BaseField calls this override during construction, before content is assigned.
        public override VisualElement contentContainer => content ?? this;

        public override bool canGrabFocus => viewBinding is not null && viewBinding.CanReceiveFocus && base.canGrabFocus;
    }
}
