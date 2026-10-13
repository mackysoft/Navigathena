using System.Threading;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary>Owns revocation for exactly one continuous input period.</summary>
    internal sealed class ViewInputGrant
    {
        private readonly UiToolkitViewBinding binding;
        private readonly CancellationTokenSource cancellation = new();
        private bool revoked;

        public ViewInputGrant (UiToolkitViewBinding binding) => this.binding = binding;

        public bool IsCurrent => !revoked && ReferenceEquals(binding.Grant, this) && binding.InputAllowed;
        public CancellationToken Revoked => revoked ? new CancellationToken(true) : cancellation.Token;

        public void Invalidate () => revoked = true;

        public void NotifyRevocation ()
        {
            try
            {
                cancellation.Cancel();
            }
            finally
            {
                cancellation.Dispose();
            }
        }
    }
}
