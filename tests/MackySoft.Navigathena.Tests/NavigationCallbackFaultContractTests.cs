using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationCallbackFaultContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Notices = new("notices");

    [Fact]
    public async Task Callback_faults_are_diagnostic_without_rolling_back_the_commit_or_skipping_later_observers ()
    {
        TestPresentationRealizer realizer = new()
        {
            ReportProgress = progress => progress.Report(NavigationPhase.Prepare, 2d),
        };
        RecordingObserver laterObserver = new();
        await using NavigationHost host = NavigationHost.Create(
            CreateDefinition(),
            realizer,
            new NavigationHostOptions(new INavigationCommitObserver[] { new ThrowingObserver(), laterObserver }));

        NavigationException result = await Assert.ThrowsAsync<NavigationException>(async () => await host.Client.ResetAsync(
            host.Root,
            Destination.For(new MainRoute()),
            new NavigationOptions(new ThrowingProgressReceiver())));

        Assert.True(result.DestinationCommitted);
        Assert.Single(laterObserver.Commits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Commit && diagnostic.Reason.Contains("observer threw", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Prepare && diagnostic.Reason.Contains("progress receiver threw", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Prepare && diagnostic.Reason.Contains("out-of-range", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_source_request_raised_by_an_observer_conflicts_with_the_still_reserved_history ()
    {
        TestPresentationRealizer realizer = new();
        ReentrantObserver observer = new(realizer);
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer, new NavigationHostOptions(new[] { observer }));
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries[0]);
        PresentationContext mainContext = Assert.Single(realizer.Contexts, context => context.EntryId == main.Id);
        observer.Action = mainContext.Navigation
            .GetRegionNavigation(RegionTarget.Child(Notices));

        RegionInstanceId notices = reset.FinalSnapshot.Regions.Values.Single(region => region.OwnerEntryId == main.Id && region.DefinitionId == Notices).Id;
        NavigationResult triggering = await host.Client.Push(notices, Destination.For(new NoticeRoute())).WaitAsync();
        NavigationResult reentrant = await observer.ReentrantOperation!;

        Assert.Equal(NavigationResultKind.Committed, triggering.Kind);
        Assert.Equal(NavigationResultKind.Conflict, reentrant.Kind);
        Assert.Equal(observer.PreparationsBeforeReentry, observer.PreparationsWhenReentryWasQueued);
        Assert.Equal(realizer.PrepareCount, observer.PreparationsWhenReentryWasQueued);
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Exclusive, root =>
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                main.AddChildRegion(Notices, RegionCompositionMode.Layered, RegionOccupancy.Optional, notices =>
                    notices.AddRoute<NoticeRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Push;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
                    }));
            }));
    }

    private sealed class ThrowingObserver : INavigationCommitObserver
    {
        public void OnCommitted (NavigationCommit commit) => throw new InvalidOperationException("observer fault");
    }

    private sealed class RecordingObserver : INavigationCommitObserver
    {
        private readonly List<NavigationCommit> commits = new();

        public IReadOnlyList<NavigationCommit> Commits => commits;

        public void OnCommitted (NavigationCommit commit) => commits.Add(commit);
    }

    private sealed class ThrowingProgressReceiver : INavigationProgressReceiver
    {
        public void Report (NavigationProgress progress) => throw new InvalidOperationException("progress fault");
    }

    private sealed class ReentrantObserver : INavigationCommitObserver
    {
        private readonly TestPresentationRealizer realizer;
        private bool hasQueuedReentry;

        public ReentrantObserver (TestPresentationRealizer realizer)
        {
            this.realizer = realizer;
        }

        public IScreenNavigation? Action
        {
            get; set;
        }
        public Task<NavigationResult>? ReentrantOperation
        {
            get; private set;
        }
        public int PreparationsBeforeReentry
        {
            get; private set;
        }
        public int PreparationsWhenReentryWasQueued
        {
            get; private set;
        }

        public void OnCommitted (NavigationCommit commit)
        {
            if (hasQueuedReentry || Action is null)
            {
                return;
            }

            hasQueuedReentry = true;
            PreparationsBeforeReentry = realizer.PrepareCount;
            ReentrantOperation = Action.Push(Destination.For(new NoticeRoute())).WaitAsync();
            PreparationsWhenReentryWasQueued = realizer.PrepareCount;
        }
    }

    private sealed record MainRoute : Route;
    private sealed record NoticeRoute : Route;
}
