using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationGroupedLossContractTests
{
    private static readonly RegionDefinitionId Root = new("root");

    [Fact]
    public void Loss_carrier_rejects_invalid_members_and_keeps_a_detached_unique_snapshot ()
    {
        NavigationEntryId firstEntry = new(Guid.NewGuid());
        NavigationEntryId secondEntry = new(Guid.NewGuid());
        PresentationId firstPresentation = new(Guid.NewGuid());
        PresentationId secondPresentation = new(Guid.NewGuid());
        PresentationReference first = new(firstEntry, firstPresentation);
        PresentationReference second = new(secondEntry, secondPresentation);
        List<PresentationReference> source = new() { first, second };

        PresentationLoss loss = new(source, "The physical backing was lost.");
        source.Clear();

        Assert.Equal(2, loss.Members.Count);
        Assert.Contains(loss.Members, member => member.EntryId == firstEntry && member.PresentationId == firstPresentation);
        Assert.Contains(loss.Members, member => member.EntryId == secondEntry && member.PresentationId == secondPresentation);
        Assert.ThrowsAny<ArgumentException>(() => new PresentationReference(default, firstPresentation));
        Assert.ThrowsAny<ArgumentException>(() => new PresentationReference(firstEntry, default));
        Assert.Throws<ArgumentNullException>(() => new PresentationLoss(null!, "The physical backing was lost."));
        Assert.Throws<ArgumentException>(() => new PresentationLoss(Array.Empty<PresentationReference>(), "The physical backing was lost."));
        Assert.Throws<ArgumentException>(() => new PresentationLoss(new PresentationReference[] { first, null! }, "The physical backing was lost."));
        Assert.Throws<ArgumentException>(() => new PresentationLoss(new[] { first, new PresentationReference(firstEntry, secondPresentation) }, "The physical backing was lost."));
        Assert.Throws<ArgumentException>(() => new PresentationLoss(new[] { first, new PresentationReference(secondEntry, firstPresentation) }, "The physical backing was lost."));
        Assert.Throws<ArgumentNullException>(() => new PresentationLoss(new[] { first }, null!));
    }

    [Fact]
    public async Task A_group_of_current_members_is_lost_in_one_revision_with_one_shared_incident ()
    {
        RecordingObserver observer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer(), new NavigationHostOptions(new[] { observer }));
        GroupFixture fixture = await CreateCurrentGroupAsync(host, includeSurvivor: true);
        NavigationState before = host.State.Current;
        Task<NavigationState> changed = host.State.WaitForChangeAsync(before.Revision).AsTask();
        int commitsBeforeLoss = observer.Commits.Count;

        PresentationLossResult result = await host.Loss.ReportAsync(fixture.Loss);
        NavigationState lost = await changed;

        Assert.Equal(PresentationLossResult.Applied, result);
        Assert.Equal(before.Revision + 1, lost.Revision);
        Assert.Equal(commitsBeforeLoss, observer.Commits.Count);
        Assert.Equal(before.GetRegion(host.Root).Entries, lost.GetRegion(host.Root).Entries);
        AssertAllMembersLostWithOneIncident(fixture.Loss, lost);
        AssertSurvivorIsUnchanged(fixture.Survivor!, before, lost);
    }

    [Fact]
    public async Task A_group_with_current_and_stale_members_loses_only_the_current_member ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        GroupFixture fixture = await CreateCurrentGroupAsync(host, includeSurvivor: false);
        NavigationResult replaced = await host.Client.Replace(host.Root, Destination.For(new ReplacementRoute())).WaitAsync();
        NavigationEntry successor = Assert.Single(replaced.Changes.CreatedEntries);
        PresentationId successorPresentation = replaced.FinalSnapshot.GetPresentation(successor.Id).Id!.Value;
        NavigationState before = host.State.Current;

        PresentationLossResult result = await host.Loss.ReportAsync(fixture.Loss);
        NavigationState after = host.State.Current;

        PresentationReference current = fixture.Loss.Members.Single(member => member.EntryId == fixture.Main.Id);
        Assert.Equal(PresentationLossResult.Applied, result);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(PresentationMaterialization.Lost, after.GetPresentation(current.EntryId).Materialization);
        Assert.Equal(PresentationMaterialization.Available, after.GetPresentation(successor.Id).Materialization);
        Assert.Equal(successorPresentation, after.GetPresentation(successor.Id).Id);
    }

    [Fact]
    public async Task A_group_whose_members_are_all_stale_is_obsolete_without_changing_the_current_state ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        GroupFixture fixture = await CreateCurrentGroupAsync(host, includeSurvivor: false);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new ReplacementRoute())).WaitAsync();
        NavigationEntry successor = Assert.Single(reset.Changes.CreatedEntries);
        PresentationId successorPresentation = reset.FinalSnapshot.GetPresentation(successor.Id).Id!.Value;
        NavigationState before = host.State.Current;

        PresentationLossResult result = await host.Loss.ReportAsync(fixture.Loss);
        NavigationState after = host.State.Current;

        Assert.Equal(PresentationLossResult.Obsolete, result);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(PresentationMaterialization.Available, after.GetPresentation(successor.Id).Materialization);
        Assert.Equal(successorPresentation, after.GetPresentation(successor.Id).Id);
    }

    [Fact]
    public async Task Recovery_restores_all_members_of_one_incident_with_fresh_presentations_and_preserves_a_survivor ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        GroupFixture fixture = await CreateCurrentGroupAsync(host, includeSurvivor: true);
        await host.Loss.ReportAsync(fixture.Loss);
        NavigationState lost = host.State.Current;
        NavigationIncidentId incident = lost.GetPresentation(fixture.Main.Id).IncidentId!.Value;
        int updatesBeforeRecovery = realizer.Updates.Count;

        NavigationResult recovery = await host.Recovery.RecoverAsync(incident);

        Assert.True(recovery.DestinationCommitted);
        Assert.Equal(lost.Revision + 1, recovery.FinalSnapshot.Revision);
        Assert.Single(realizer.Updates.Skip(updatesBeforeRecovery), update => update.Kind == PresentationUpdateKind.Restoration);
        PresentationId[] recoveredIds = fixture.Loss.Members
            .Select(member => recovery.FinalSnapshot.GetPresentation(member.EntryId).Id!.Value)
            .ToArray();
        Assert.Equal(fixture.Loss.Members.Count, recoveredIds.Distinct().Count());
        foreach (PresentationReference member in fixture.Loss.Members)
        {
            PresentationState recovered = recovery.FinalSnapshot.GetPresentation(member.EntryId);
            Assert.Equal(PresentationMaterialization.Available, recovered.Materialization);
            Assert.Null(recovered.IncidentId);
            Assert.NotEqual(member.PresentationId, recovered.Id);
        }

        AssertSurvivorIsUnchanged(fixture.Survivor!, lost, recovery.FinalSnapshot);
    }

    [Fact]
    public async Task Recovery_failure_before_apply_keeps_the_entire_incident_lost_and_preserves_a_survivor ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        GroupFixture fixture = await CreateCurrentGroupAsync(host, includeSurvivor: true);
        await host.Loss.ReportAsync(fixture.Loss);
        NavigationState lost = host.State.Current;
        NavigationIncidentId incident = lost.GetPresentation(fixture.Main.Id).IncidentId!.Value;
        realizer.RejectRestorationCommit = true;

        NavigationResult recovery = await host.Recovery.RecoverAsync(incident);

        Assert.False(recovery.DestinationCommitted);
        AssertAllMembersRemainLostForIncident(fixture.Loss, incident, recovery.FinalSnapshot);
        AssertSurvivorIsUnchanged(fixture.Survivor!, lost, recovery.FinalSnapshot);
    }

    [Fact]
    public async Task Recovery_cancellation_before_apply_keeps_the_entire_incident_lost_and_preserves_a_survivor ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        GroupFixture fixture = await CreateCurrentGroupAsync(host, includeSurvivor: true);
        await host.Loss.ReportAsync(fixture.Loss);
        NavigationState lost = host.State.Current;
        NavigationIncidentId incident = lost.GetPresentation(fixture.Main.Id).IncidentId!.Value;
        Task preparationBlocked = realizer.BlockNextPreparationAsync();
        using CancellationTokenSource cancellation = new();

        Task<NavigationResult> recoveryTask = host.Recovery.RecoverAsync(incident, cancellation.Token).AsTask();
        await preparationBlocked;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recoveryTask);
        AssertAllMembersRemainLostForIncident(fixture.Loss, incident, host.State.Current);
        AssertSurvivorIsUnchanged(fixture.Survivor!, lost, host.State.Current);
    }

    private static async Task<GroupFixture> CreateCurrentGroupAsync (NavigationHost host, bool includeSurvivor)
    {
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute())).WaitAsync();
        NavigationEntry main = Assert.Single(reset.Changes.CreatedEntries);
        NavigationResult overlayNavigation = await host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        NavigationEntry overlay = Assert.Single(overlayNavigation.Changes.CreatedEntries);
        NavigationEntry? survivor = null;
        if (includeSurvivor)
        {
            NavigationResult survivorNavigation = await host.Client.Push(host.Root, Destination.For(new SurvivorRoute())).WaitAsync();
            survivor = Assert.Single(survivorNavigation.Changes.CreatedEntries);
        }

        NavigationState state = host.State.Current;
        PresentationLoss loss = new(
            new[]
            {
                new PresentationReference(main.Id, state.GetPresentation(main.Id).Id!.Value),
                new PresentationReference(overlay.Id, state.GetPresentation(overlay.Id).Id!.Value),
            },
            "The physical backing was lost.");
        return new GroupFixture(loss, main, survivor);
    }

    private static void AssertAllMembersLostWithOneIncident (PresentationLoss loss, NavigationState state)
    {
        NavigationIncidentId incident = state.GetPresentation(loss.Members[0].EntryId).IncidentId!.Value;
        AssertAllMembersRemainLostForIncident(loss, incident, state);
    }

    private static void AssertAllMembersRemainLostForIncident (PresentationLoss loss, NavigationIncidentId incident, NavigationState state)
    {
        foreach (PresentationReference member in loss.Members)
        {
            PresentationState presentation = state.GetPresentation(member.EntryId);
            Assert.Equal(PresentationMaterialization.Lost, presentation.Materialization);
            Assert.Equal(member.PresentationId, presentation.Id);
            Assert.Equal(incident, presentation.IncidentId);
        }
    }

    private static void AssertSurvivorIsUnchanged (NavigationEntry survivor, NavigationState before, NavigationState after)
    {
        PresentationState beforePresentation = before.GetPresentation(survivor.Id);
        PresentationState afterPresentation = after.GetPresentation(survivor.Id);
        Assert.Equal(PresentationMaterialization.Available, afterPresentation.Materialization);
        Assert.Equal(beforePresentation.Id, afterPresentation.Id);
        Assert.Equal(beforePresentation.IncidentId, afterPresentation.IncidentId);
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
            root.AddRoute<ReplacementRoute>(replacement =>
            {
                replacement.AllowedEntryOperations = RouteEntryOperations.Reset | RouteEntryOperations.Replace;
                replacement.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            root.AddRoute<SurvivorRoute>(survivor =>
            {
                survivor.AllowedEntryOperations = RouteEntryOperations.Push;
                survivor.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
        });
    }

    private sealed record GroupFixture (PresentationLoss Loss, NavigationEntry Main, NavigationEntry? Survivor);

    private sealed class RecordingObserver : INavigationCommitObserver
    {
        private readonly List<NavigationCommit> commits = new();

        public IReadOnlyList<NavigationCommit> Commits => commits;

        public void OnCommitted (NavigationCommit commit) => commits.Add(commit);
    }

    private sealed record MainRoute : Route;
    private sealed record OverlayRoute : Route;
    private sealed record ReplacementRoute : Route;
    private sealed record SurvivorRoute : Route;
}
