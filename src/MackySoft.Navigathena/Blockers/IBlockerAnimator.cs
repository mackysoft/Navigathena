using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Animates blocker decoration without controlling view permissions or navigation.</summary>
    /// <remarks>The runtime checks cancellation immediately before and after asynchronous animation. Implementations forward the token to ongoing work without duplicating the entry check. Superseded animation is awaited before releasing resources, but must not overwrite the replacement appearance.</remarks>
    public interface IBlockerAnimator
    {
        ValueTask PlayEnterAsync (CancellationToken cancellationToken);
        ValueTask PlayExitAsync (CancellationToken cancellationToken);
        /// <summary>Invalidates prior animation writes synchronously and sets its final appearance.</summary>
        void SetAppearanceImmediately (bool shown);
    }
}
