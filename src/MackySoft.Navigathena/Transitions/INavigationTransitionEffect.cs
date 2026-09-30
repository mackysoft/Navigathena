using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Implements visual choreography without controlling navigation, screen activity, or resource release.</summary>
    /// <remarks>
    /// The runtime checks the supplied token immediately before and after BeginAsync, PrepareSwitchAsync and AfterCommitAsync.
    /// Implementations forward it to ongoing work without duplicating the entry check. After commit, cancellation does not undo history.
    /// SettleAsync is cleanup: the runtime always awaits it with CancellationToken.None, including after cancellation.
    /// Activation and input permission follow successful settlement. With AfterResourceRelease timing, keep a loading overlay
    /// visible during AfterCommitAsync and hide it in SettleAsync, after retired screens and their resources finish ending.
    /// </remarks>
    public interface INavigationTransitionEffect
    {
        ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken);
        ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken);
        ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken);
        ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken);
    }
}
