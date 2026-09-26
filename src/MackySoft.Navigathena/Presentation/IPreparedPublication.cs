using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Owns one successfully prepared update until it is adopted or disposed.</summary>
    internal interface IPreparedPublication : IAsyncDisposable
    {
        ValueTask<PublicationOutcome> CommitAsync (INavigationCommit commit, CancellationToken shutdownToken);
        ValueTask<PresentationCompletion> CompleteAsync (CancellationToken shutdownToken);
    }

}
