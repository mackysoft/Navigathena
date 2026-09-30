using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Constructs a blocker inside runtime-owned resources, including when construction fails.</summary>
    /// <remarks>The runtime checks cancellation before invocation and after completion. The factory forwards the token to ongoing work; it need not repeat the entry check. Cancellation does not skip cleanup of partial construction.</remarks>
    public delegate ValueTask<IBlockerPresenter> BlockerFactory (BlockerPreparationContext preparation, CancellationToken cancellationToken);
}
