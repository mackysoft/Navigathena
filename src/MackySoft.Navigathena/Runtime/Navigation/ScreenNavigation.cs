using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Navigation
{
    internal sealed class ScreenNavigation : IScreenNavigation
    {
        private readonly INavigationStateSource state;
        private readonly ISourceNavigationRequestPort requests;
        private readonly ScreenNavigationScope scope;
        private readonly NavigationEntryId entryId;
        private readonly PresentationId presentationId;
        private readonly RegionInstanceId target;
        private readonly bool explicitTarget;

        public ScreenNavigation (INavigationStateSource state, ISourceNavigationRequestPort requests, NavigationState proposed, NavigationEntryId entryId, PresentationId presentationId)
            : this(state, requests, new ScreenNavigationScope(proposed, entryId), entryId, presentationId, proposed.GetEntry(entryId).RegionId)
        {
        }

        private ScreenNavigation (INavigationStateSource state, ISourceNavigationRequestPort requests, ScreenNavigationScope scope, NavigationEntryId entryId, PresentationId presentationId, RegionInstanceId target, bool explicitTarget = false)
        {
            this.state = state;
            this.requests = requests;
            this.scope = scope;
            this.entryId = entryId;
            this.presentationId = presentationId;
            this.target = target;
            this.explicitTarget = explicitTarget;
        }

        public Task InvokeAsync (Route route, CancellationToken cancellationToken = default, NavigationOptions? options = null)
            => requests.InvokeAsync(entryId, presentationId, target, route, cancellationToken, options);

        public Task<TResult> InvokeAsync<TResult> (Route<TResult> route, CancellationToken cancellationToken = default, NavigationOptions? options = null)
            => requests.InvokeAsync(entryId, presentationId, target, route, cancellationToken, options);

        public NavigationOperation Push<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Execute(NavigationOperationKind.Push, destination ?? throw new ArgumentNullException(nameof(destination)), options);
        public NavigationOperation ReplaceFrom<TRoute> (HistoryTarget target, NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Execute(NavigationOperationKind.Replace, destination ?? throw new ArgumentNullException(nameof(destination)), options, target ?? throw new ArgumentNullException(nameof(target)));
        public NavigationOperation Reset<TRoute> (NavigationDestinationTree<TRoute> destination, NavigationOptions? options = null) where TRoute : Route => Execute(NavigationOperationKind.Reset, destination ?? throw new ArgumentNullException(nameof(destination)), options);
        public NavigationOperation Back (BackOptions? options = null) => Execute(NavigationOperationKind.Back, null, options?.ToNavigationOptions());
        public NavigationOperation Reload (ReloadOptions? options = null) => Execute(NavigationOperationKind.Reload, null, (options ?? new ReloadOptions()).ToNavigationOptions());
        public void PostPush<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => requests.Post(entryId, presentationId, () => Push(Destination.For(route), options));
        public void PostReplace<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => requests.Post(entryId, presentationId, () => ReplaceFrom(HistoryTarget.Current, Destination.For(route), options));
        public void PostReset<TRoute> (TRoute route, NavigationOptions? options = null) where TRoute : Route => requests.Post(entryId, presentationId, () => Reset(Destination.For(route), options));
        public void PostBack (BackOptions? options = null) => requests.Post(entryId, presentationId, () => Back(options));

        public IScreenNavigation GetRegionNavigation (RegionTarget target) => new ScreenNavigation(state, requests, scope, entryId, presentationId, scope.Resolve(target), true);


        private NavigationOperation Execute (NavigationOperationKind kind, INavigationDestinationTree? destination, NavigationOptions? options, HistoryTarget? replacement = null)
        {
            NavigationState snapshot = state.Current;
            if (!new SourcePrecondition(entryId, presentationId).IsCurrent(snapshot)
                || !snapshot.Regions.TryGetValue(target, out RegionState? region))
            {
                return Reject(kind, snapshot, "The source screen or its target region is no longer available.");
            }

            NavigationEntryId? expectedTop = region.Entries.LastOrDefault();
            if ((kind == NavigationOperationKind.Reset || replacement?.IsCurrent == false) && !explicitTarget && snapshot.GetEntry(entryId).CallId.HasValue)
            {
                throw new NavigationConfigurationException("Use the typed call navigation to continue answering, or explicitly target the outer region to abandon the call.");
            }
            if ((kind == NavigationOperationKind.Back || (kind == NavigationOperationKind.Replace && replacement?.IsCurrent != false) || kind == NavigationOperationKind.Reload)
                && scope.TryGetOwner(target, out NavigationEntryId owner))
            {
                // A retained but covered screen may issue navigation, but closing itself
                // must never remove the different screen now above it.
                expectedTop = owner;
                if (region.Entries.LastOrDefault() != owner)
                {
                    return Reject(kind, snapshot, "Only the current screen can close or replace itself.");
                }
            }

            return requests.Request(new NavigationRequest(kind, target, destination, options, CancellationToken.None, new SourcePrecondition(entryId, presentationId, target, expectedTop))
            {
                ReplacementTarget = replacement
            });
        }

        private static NavigationOperation Reject (NavigationOperationKind kind, NavigationState snapshot, string reason) => NavigationOperation.FromResult(new NavigationResult(new NavigationOperationId(Guid.NewGuid()), kind, NavigationResultKind.Rejected, false, snapshot, NavigationDelta.Empty, RestorationOutcome.NotRequired, new[] { new NavigationDiagnostic(NavigationPhase.Prepare, reason) }));
    }
}
