using System.Threading;

namespace MackySoft.Navigathena.Unity.UIToolkit
{
    /// <summary> An immutable observation of one screen input permission period. </summary>
    /// <remarks> Keep the same handle across awaits. Query IsValid on the Unity player thread. A default handle is invalid. This handle does not operate UI controls. </remarks>
    public readonly struct UiInputSession
    {
        private readonly ViewInputGrant? grant;

        internal UiInputSession (ViewInputGrant grant)
        {
            this.grant = grant;
        }

        /// <summary> Whether this captured permission period is still open. </summary>
        public bool IsValid => grant is not null && grant.IsCurrent;
        /// <summary> Canceled when this captured permission period ends. </summary>
        public CancellationToken Revoked => grant is not null ? grant.Revoked : new CancellationToken(true);
    }
}
