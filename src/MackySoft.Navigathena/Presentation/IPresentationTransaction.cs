using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Presentation
{

    /// <summary>Owns staging resources and physical reservations for one navigation operation.</summary>
    internal interface IPresentationTransaction : IAsyncDisposable
    {
        IReadOnlyList<NavigationEntryId> DepartureEntries
        {
            get;
        }
        ValueTask<IPreparedPublication> PrepareAsync (PresentationUpdate update, CancellationToken cancellationToken);
    }

}
