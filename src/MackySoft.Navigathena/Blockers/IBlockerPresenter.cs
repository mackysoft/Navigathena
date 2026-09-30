using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Provides custom blocker content. The runtime owns preparation, connection and termination.</summary>
    /// <remarks>The runtime checks cancellation immediately before and after PrepareAsync. Implementations forward the token to ongoing work without duplicating the entry check. TerminateAsync is awaited even after cancellation.</remarks>
    public interface IBlockerPresenter
    {
        ValueTask PrepareAsync (BlockerPreparationContext preparation, CancellationToken cancellationToken);
        ValueTask TerminateAsync ();
        void SetScreenContext (BlockerScreenContext? context);
    }
}
