using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{

    internal interface ISourceNavigationRequestPort
    {
        NavigationOperation Request (NavigationRequest request);
        void Post (NavigationEntryId entry, PresentationId presentation, Func<NavigationOperation> request);
        Task InvokeAsync (NavigationEntryId owner, PresentationId presentation, RegionInstanceId target, Route route, CancellationToken cancellationToken, NavigationOptions? options);
        Task<TResult> InvokeAsync<TResult> (NavigationEntryId owner, PresentationId presentation, RegionInstanceId target, Route<TResult> route, CancellationToken cancellationToken, NavigationOptions? options);
    }

}
