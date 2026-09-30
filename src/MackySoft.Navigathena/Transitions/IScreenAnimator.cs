using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Animates one screen; it does not control its activity, history, or lifetime.</summary>
    /// <remarks>The runtime checks cancellation before and after playback. Completion includes stopping every animation writer, also on failure or cancellation. Implementations forward cancellation to ongoing work; they never change input or resource ownership.</remarks>
    public interface IScreenAnimator
    {
        void SetStateImmediately (ScreenAnimationState state);
        ValueTask PlayAsync (ScreenAnimation animation, CancellationToken cancellationToken);
    }
}
