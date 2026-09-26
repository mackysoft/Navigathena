using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationLossCancellationContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public async Task A_pre_cancelled_current_loss_cancels_the_caller_wait_but_is_applied_once ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        PresentationLoss loss = await ResetAndCreateCurrentLossAsync(host);
        NavigationState before = host.State.Current;
        Task<NavigationState> changed = host.State.WaitForChangeAsync(before.Revision).AsTask();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => host.Loss.ReportAsync(loss, cancellation.Token).AsTask());
        NavigationState lost = await changed;
        PresentationLossResult repeated = await host.Loss.ReportAsync(loss);

        AssertAllMembersLostWithOneIncident(loss, lost);
        Assert.Equal(PresentationLossResult.Obsolete, repeated);
        Assert.Equal(lost.Revision, host.State.Current.Revision);
    }

    [Fact]
    public async Task Caller_cancellation_during_an_observer_callback_does_not_withdraw_the_accepted_loss ()
    {
        BlockingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer(), new NavigationHostOptions(new[] { observer }));
        PresentationLoss loss = await ResetAndCreateCurrentLossAsync(host);
        observer.BlockNextCommit();

        Task<NavigationResult> navigation = host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        await observer.WaitForBlockAsync();
        NavigationState beforeLoss = host.State.Current;
        Task<NavigationState> changed = host.State.WaitForChangeAsync(beforeLoss.Revision).AsTask();
        using CancellationTokenSource cancellation = new();
        Task<PresentationLossResult> report = host.Loss.ReportAsync(loss, cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => report);
        observer.Release();
        await navigation;
        NavigationState lost = await changed;

        AssertAllMembersLostWithOneIncident(loss, lost);
    }

    [Fact]
    public async Task A_completed_loss_result_is_not_changed_by_later_caller_cancellation ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        PresentationLoss loss = await ResetAndCreateCurrentLossAsync(host);
        using CancellationTokenSource appliedCancellation = new();

        PresentationLossResult applied = await host.Loss.ReportAsync(loss, appliedCancellation.Token);
        NavigationState lost = host.State.Current;
        appliedCancellation.Cancel();

        using CancellationTokenSource obsoleteCancellation = new();
        PresentationLossResult obsolete = await host.Loss.ReportAsync(loss, obsoleteCancellation.Token);
        obsoleteCancellation.Cancel();

        Assert.Equal(PresentationLossResult.Applied, applied);
        Assert.Equal(PresentationLossResult.Obsolete, obsolete);
        Assert.Equal(lost.Revision, host.State.Current.Revision);
        AssertAllMembersLostWithOneIncident(loss, host.State.Current);
    }

    [Fact]
    public async Task A_pre_cancelled_loss_on_a_closed_host_returns_runtime_closed ()
    {
        NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        PresentationLoss loss = await ResetAndCreateCurrentLossAsync(host);
        await host.DisposeAsync();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        PresentationLossResult result = await host.Loss.ReportAsync(loss, cancellation.Token);

        Assert.Equal(PresentationLossResult.RuntimeClosed, result);
    }

    [Fact]
    public async Task A_loss_applied_before_shutdown_remains_lost_after_the_host_closes ()
    {
        NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        PresentationLoss loss = await ResetAndCreateCurrentLossAsync(host);

        PresentationLossResult result = await host.Loss.ReportAsync(loss);
        NavigationState lost = host.State.Current;
        await host.DisposeAsync();

        Assert.Equal(PresentationLossResult.Applied, result);
        Assert.Equal(lost.Revision, host.State.Current.Revision);
        AssertAllMembersLostWithOneIncident(loss, host.State.Current);
    }

    private static async Task<PresentationLoss> ResetAndCreateCurrentLossAsync (NavigationHost host)
    {
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries[0]);
        NavigationResult pushed = await host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        NavigationEntry overlay = pushed.FinalSnapshot.GetEntry(pushed.FinalSnapshot.GetRegion(host.Root).Entries.Last());
        return new PresentationLoss(
            new[]
            {
                new PresentationReference(main.Id, reset.FinalSnapshot.GetPresentation(main.Id).Id!.Value),
                new PresentationReference(overlay.Id, pushed.FinalSnapshot.GetPresentation(overlay.Id).Id!.Value),
            },
            "The presentation was lost.");
    }

    private static void AssertAllMembersLostWithOneIncident (PresentationLoss loss, NavigationState state)
    {
        NavigationIncidentId incident = state.GetPresentation(loss.Members[0].EntryId).IncidentId!.Value;
        foreach (PresentationReference member in loss.Members)
        {
            PresentationState presentation = state.GetPresentation(member.EntryId);
            Assert.Equal(PresentationMaterialization.Lost, presentation.Materialization);
            Assert.Equal(member.PresentationId, presentation.Id);
            Assert.Equal(incident, presentation.IncidentId);
        }
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            root.AddRoute<OverlayRoute>(overlay =>
            {
                overlay.AllowedEntryOperations = RouteEntryOperations.Push;
                overlay.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
        });
    }

    private sealed class BlockingObserver : INavigationCommitObserver
    {
        private TaskCompletionSource<bool>? blocked;
        private TaskCompletionSource<bool>? release;

        public void BlockNextCommit ()
        {
            blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForBlockAsync () => blocked!.Task;

        public void Release () => release!.TrySetResult(true);

        public void OnCommitted (NavigationCommit commit)
        {
            if (blocked is null || release is null)
            {
                return;
            }

            blocked.TrySetResult(true);
            release.Task.GetAwaiter().GetResult();
            blocked = null;
            release = null;
        }
    }

    private sealed record MainRoute : Route;
    private sealed record OverlayRoute : Route;
}
