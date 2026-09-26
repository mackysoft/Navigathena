using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class PresentationCarrierContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Tabs = new("tabs");
    private static readonly RegionDefinitionId Notices = new("notices");
    private static readonly RegionDefinitionId Sibling = new("sibling");

    [Fact]
    public async Task Transition_and_update_carry_the_composition_and_input_boundary_of_their_corresponding_states ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new HomeRoute()))).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId notices = FindChildRegion(reset.FinalSnapshot, main.Id, Notices);
        NavigationResult notice = await host.Client.Push(notices, Destination.For(new NoticeRoute())).WaitAsync();
        NavigationEntry noticeEntry = notice.FinalSnapshot.GetEntry(notice.FinalSnapshot.GetRegion(notices).Entries.Single());

        NavigationResult overlay = await host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        PresentationTransition transition = realizer.Transitions[^1];
        PresentationUpdate update = realizer.Updates[^1];
        NavigationEntry overlayEntry = overlay.FinalSnapshot.GetEntry(overlay.FinalSnapshot.GetRegion(host.Root).Entries.Last());
        NavigationEntry homeEntry = overlay.FinalSnapshot.GetEntry(overlay.FinalSnapshot.GetRegion(FindChildRegion(overlay.FinalSnapshot, main.Id, Tabs)).Entries.Single());

        AssertComposition(
            transition.BeforeComposition,
            new[]
            {
                (main.Id, new PresentationParticipation(true, true, true, true)),
                (homeEntry.Id, new PresentationParticipation(true, true, true, true)),
                (noticeEntry.Id, new PresentationParticipation(true, true, true, true)),
            },
            Array.Empty<InputBoundary>());
        AssertComposition(
            transition.ProposedComposition,
            new[]
            {
                (main.Id, new PresentationParticipation(false, false, false, false)),
                (homeEntry.Id, new PresentationParticipation(false, false, false, false)),
                (noticeEntry.Id, new PresentationParticipation(false, false, false, false)),
                (overlayEntry.Id, new PresentationParticipation(true, true, true, true)),
            },
            new[] { new InputBoundary(host.Root, overlayEntry.Id) });
        AssertComposition(update.BeforeComposition, transition.BeforeComposition.Presentations.Select(presentation => (presentation.EntryId, presentation.Participation)), transition.BeforeComposition.InputBoundaries);
        AssertComposition(update.ProposedComposition, transition.ProposedComposition.Presentations.Select(presentation => (presentation.EntryId, presentation.Participation)), transition.ProposedComposition.InputBoundaries);
    }

    [Fact]
    public async Task Retained_parent_updates_include_only_the_changed_direct_child_history_once ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new HomeRoute()))).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        PresentationId mainPresentation = reset.FinalSnapshot.GetPresentation(main.Id).Id!.Value;
        RegionInstanceId tabs = FindChildRegion(reset.FinalSnapshot, main.Id, Tabs);
        RegionInstanceId notices = FindChildRegion(reset.FinalSnapshot, main.Id, Notices);

        NavigationResult pushed = await host.Client.Push(notices, Destination.For(new NoticeRoute())).WaitAsync();
        AssertRetainedChildChange(realizer.CommittedPublications[^1].Update, main, mainPresentation, Notices, notices, Array.Empty<NavigationEntryId>(), pushed.FinalSnapshot.GetRegion(notices).Entries, typeof(NoticeRoute));

        NavigationResult replaced = await host.Client.Replace(tabs, Destination.For(new SettingsRoute())).WaitAsync();
        AssertRetainedChildChange(realizer.CommittedPublications[^1].Update, main, mainPresentation, Tabs, tabs, reset.FinalSnapshot.GetRegion(tabs).Entries, replaced.FinalSnapshot.GetRegion(tabs).Entries, typeof(SettingsRoute));

        NavigationResult cleared = await host.Client.Clear(notices).WaitAsync();
        AssertRetainedChildChange(realizer.CommittedPublications[^1].Update, main, mainPresentation, Notices, notices, pushed.FinalSnapshot.GetRegion(notices).Entries, cleared.FinalSnapshot.GetRegion(notices).Entries, null);

        int committedChanges = realizer.CommittedPublications.Count;
        realizer.RejectDestinationCommit = true;
        NavigationResult conflict = await host.Client.Push(notices, Destination.For(new NoticeRoute())).WaitAsync();

        Assert.Equal(NavigationResultKind.Conflict, conflict.Kind);
        Assert.Equal(committedChanges, realizer.CommittedPublications.Count);

        Task blockedPreparation = realizer.BlockNextPreparationAsync();
        NavigationOperation cancelledOperation = host.Client.Push(notices, Destination.For(new NoticeRoute()));
        await blockedPreparation;
        Assert.True(cancelledOperation.TryRequestCancellation());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledOperation.WaitAsync());
        Assert.Equal(committedChanges, realizer.CommittedPublications.Count);
    }

    [Fact]
    public async Task Caller_cancellation_after_apply_does_not_revert_the_committed_candidate ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new HomeRoute()))).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId notices = FindChildRegion(reset.FinalSnapshot, main.Id, Notices);
        Task blockedCompletion = realizer.BlockNextCompletionAsync();

        NavigationOperation operation = host.Client.Push(notices, Destination.For(new NoticeRoute()));
        await blockedCompletion;
        Assert.False(operation.TryRequestCancellation());
        realizer.ReleaseBlockedCompletion();
        NavigationResult result = await operation.WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, result.Kind);
        Assert.True(result.DestinationCommitted);
        Assert.IsType<NoticeRoute>(result.FinalSnapshot.GetEntry(result.FinalSnapshot.GetRegion(notices).Entries.Single()).Route);
    }

    [Fact]
    public async Task A_host_incident_report_is_recorded_once_rejects_normal_navigation_and_is_cleared_only_after_a_candidate_publication ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, MainDestination()).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        PresentationReference member = new(main.Id, reset.FinalSnapshot.GetPresentation(main.Id).Id!.Value);
        NavigationHostIncident incident = new(new NavigationIncidentId(Guid.NewGuid()), "The root equipment was lost.", new[] { member });

        HostIncidentReportResult first = await host.HostIncidents.ReportAsync(incident);
        long revisionAfterFirstReport = host.State.Current.Revision;
        HostIncidentReportResult duplicate = await host.HostIncidents.ReportAsync(incident);
        NavigationResult rejected = await host.Client.Push(host.Root, Destination.For(new OverlayRoute())).WaitAsync();
        Task<NavigationState> candidateObserved = host.State.WaitForChangeAsync(revisionAfterFirstReport).AsTask();
        Task<NavigationResult> recovery = host.Recovery.RecoverAsync(incident.Id).AsTask();
        NavigationState candidate = await candidateObserved;
        NavigationResult recovered = await recovery;

        Assert.Equal(HostIncidentReportResult.Applied, first);
        Assert.Equal(HostIncidentReportResult.AlreadyCurrent, duplicate);
        Assert.Equal(reset.FinalSnapshot.Revision + 1, revisionAfterFirstReport);
        Assert.Equal(NavigationResultKind.Rejected, rejected.Kind);
        Assert.Equal(revisionAfterFirstReport, rejected.FinalSnapshot.Revision);
        Assert.Empty(rejected.Changes.CreatedEntries);
        Assert.Empty(rejected.Changes.RemovedEntries);
        Assert.Equal(revisionAfterFirstReport + 1, candidate.Revision);
        Assert.Equal(incident.Id, candidate.HostIncident?.Id);
        Assert.NotEqual(member.PresentationId, candidate.GetPresentation(main.Id).Id!.Value);
        Assert.Equal(NavigationResultKind.Committed, recovered.Kind);
        Assert.True(recovered.DestinationCommitted);
        Assert.Equal(revisionAfterFirstReport + 2, recovered.FinalSnapshot.Revision);
        Assert.Null(recovered.FinalSnapshot.HostIncident);
    }

    [Fact]
    public async Task A_same_incident_report_with_a_new_current_member_extends_the_required_set_once ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, MainDestination()).WaitAsync();
        NavigationState state = reset.FinalSnapshot;
        NavigationEntry main = state.GetEntry(state.GetRegion(host.Root).Entries.Single());
        NavigationEntry home = state.GetEntry(state.GetRegion(FindChildRegion(state, main.Id, Tabs)).Entries.Single());
        PresentationReference mainMember = new(main.Id, state.GetPresentation(main.Id).Id!.Value);
        PresentationReference homeMember = new(home.Id, state.GetPresentation(home.Id).Id!.Value);
        NavigationIncidentId incidentId = new(Guid.NewGuid());
        NavigationHostIncident firstIncident = new(incidentId, "The root equipment was lost.", new[] { mainMember });
        NavigationHostIncident expandedIncident = new(incidentId, "A later report must not replace the original reason.", new[] { mainMember, homeMember });

        HostIncidentReportResult first = await host.HostIncidents.ReportAsync(firstIncident);
        long revisionBeforeExpansion = host.State.Current.Revision;
        HostIncidentReportResult expanded = await host.HostIncidents.ReportAsync(expandedIncident);
        NavigationState expandedState = host.State.Current;
        HostIncidentReportResult duplicate = await host.HostIncidents.ReportAsync(expandedIncident);

        Assert.Equal(HostIncidentReportResult.Applied, first);
        Assert.Equal(HostIncidentReportResult.AlreadyCurrent, expanded);
        Assert.Equal(revisionBeforeExpansion + 1, expandedState.Revision);
        Assert.Equal(firstIncident.Reason, expandedState.HostIncident?.Reason);
        Assert.Equal(2, expandedState.HostIncident!.RequiredPresentations.Count);
        Assert.Contains(mainMember, expandedState.HostIncident.RequiredPresentations);
        Assert.Contains(homeMember, expandedState.HostIncident.RequiredPresentations);
        Assert.Equal(HostIncidentReportResult.AlreadyCurrent, duplicate);
        Assert.Equal(expandedState.Revision, host.State.Current.Revision);
    }

    [Fact]
    public async Task A_wrong_host_recovery_is_rejected_without_changing_the_current_incident ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationHostIncident incident = await ReportRootHostIncidentAsync(host);
        NavigationState before = host.State.Current;

        NavigationResult recovery = await host.Recovery.RecoverAsync(new NavigationIncidentId(Guid.NewGuid()));

        Assert.Equal(NavigationResultKind.Rejected, recovery.Kind);
        Assert.False(recovery.DestinationCommitted);
        Assert.Equal(before.Revision, recovery.FinalSnapshot.Revision);
        Assert.Equal(incident.Id, recovery.FinalSnapshot.HostIncident?.Id);
    }

    [Fact]
    public async Task A_cancelled_host_recovery_keeps_its_current_incident_and_logical_before_state ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationHostIncident incident = await ReportHostIncidentForCurrentMainAsync(host);
        NavigationState before = host.State.Current;
        Task preparationBlocked = realizer.BlockNextPreparationAsync();
        using CancellationTokenSource cancellation = new();

        Task<NavigationResult> recoveryTask = host.Recovery.RecoverAsync(incident.Id, cancellation.Token).AsTask();
        await preparationBlocked;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recoveryTask);
        Assert.Equal(before.Revision, host.State.Current.Revision);
        Assert.Equal(incident.Id, host.State.Current.HostIncident?.Id);
    }

    [Fact]
    public async Task A_concurrent_host_recovery_is_conflict_without_clearing_the_current_incident ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationHostIncident incident = await ReportHostIncidentForCurrentMainAsync(host);
        NavigationState before = host.State.Current;
        Task preparationBlocked = realizer.BlockNextPreparationAsync();

        Task<NavigationResult> firstRecovery = host.Recovery.RecoverAsync(incident.Id).AsTask();
        await preparationBlocked;
        NavigationResult concurrentRecovery = await host.Recovery.RecoverAsync(incident.Id);
        realizer.ReleaseBlockedPreparation();
        await firstRecovery;

        Assert.Equal(NavigationResultKind.Conflict, concurrentRecovery.Kind);
        Assert.False(concurrentRecovery.DestinationCommitted);
        Assert.Equal(before.Revision, concurrentRecovery.FinalSnapshot.Revision);
        Assert.Equal(incident.Id, concurrentRecovery.FinalSnapshot.HostIncident?.Id);
    }

    [Fact]
    public async Task A_host_failure_after_recovery_apply_keeps_the_incident_with_the_fresh_presentation ()
    {
        CoordinatedPresentationRealizer realizer = new()
        {
            FailHostRecoveryAfterApply = true
        };
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationHostIncident incident = await ReportHostIncidentForCurrentMainAsync(host);
        NavigationState before = host.State.Current;
        PresentationReference beforeMember = Assert.Single(before.HostIncident!.RequiredPresentations);

        NavigationException recovery = await Assert.ThrowsAsync<NavigationException>(async () => await host.Recovery.RecoverAsync(incident.Id));
        PresentationReference afterMember = Assert.Single(recovery.FinalSnapshot.HostIncident!.RequiredPresentations);

        Assert.True(recovery.DestinationCommitted);
        Assert.Equal(before.Revision + 1, recovery.FinalSnapshot.Revision);
        Assert.Equal(incident.Id, recovery.FinalSnapshot.HostIncident?.Id);
        Assert.Equal(beforeMember.EntryId, afterMember.EntryId);
        Assert.NotEqual(beforeMember.PresentationId, afterMember.PresentationId);
        Assert.Contains(recovery.Diagnostics, diagnostic => diagnostic.Phase == NavigationPhase.Restore);
    }

    [Fact]
    public async Task A_same_host_incident_requirement_accepted_after_its_candidate_publication_prevents_clear ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        (NavigationHostIncident incident, PresentationReference additionalMember) = await ReportHostIncidentForMainWithChildAsync(host);
        long incidentRevision = host.State.Current.Revision;
        Task completionBlocked = realizer.BlockNextCompletionAsync();
        Task<NavigationState> candidateObserved = host.State.WaitForChangeAsync(incidentRevision).AsTask();
        Task<NavigationResult> recoveryTask = host.Recovery.RecoverAsync(incident.Id).AsTask();
        NavigationState candidate = await candidateObserved;
        await completionBlocked;
        Task<NavigationState> expandedObserved = host.State.WaitForChangeAsync(candidate.Revision).AsTask();
        NavigationHostIncident expanded = new(incident.Id, incident.Reason, new[] { additionalMember });
        Task<HostIncidentReportResult> reportTask = host.HostIncidents.ReportAsync(expanded).AsTask();
        NavigationState expandedState = await expandedObserved;

        Assert.Equal(incident.Id, candidate.HostIncident?.Id);
        Assert.NotEqual(incident.RequiredPresentations.Single().PresentationId, candidate.GetPresentation(incident.RequiredPresentations.Single().EntryId).Id!.Value);
        Assert.Equal(HostIncidentReportResult.AlreadyCurrent, await reportTask);
        Assert.Equal(incident.Id, expandedState.HostIncident?.Id);
        Assert.Contains(additionalMember, expandedState.HostIncident!.RequiredPresentations);

        realizer.ReleaseBlockedCompletion();
        NavigationResult recovery = await recoveryTask;

        Assert.Equal(NavigationResultKind.Committed, recovery.Kind);
        Assert.Equal(expandedState.Revision, recovery.FinalSnapshot.Revision);
        Assert.Equal(incident.Id, recovery.FinalSnapshot.HostIncident?.Id);
        Assert.Contains(additionalMember, recovery.FinalSnapshot.HostIncident!.RequiredPresentations);
    }

    [Fact]
    public async Task A_different_host_incident_accepted_after_a_candidate_publication_replaces_it_without_an_incident_free_snapshot ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        (NavigationHostIncident incident, PresentationReference additionalMember) = await ReportHostIncidentForMainWithChildAsync(host);
        long incidentRevision = host.State.Current.Revision;
        Task completionBlocked = realizer.BlockNextCompletionAsync();
        Task<NavigationState> candidateObserved = host.State.WaitForChangeAsync(incidentRevision).AsTask();
        Task<NavigationResult> recoveryTask = host.Recovery.RecoverAsync(incident.Id).AsTask();
        NavigationState candidate = await candidateObserved;
        await completionBlocked;
        Task<NavigationState> handoffObserved = host.State.WaitForChangeAsync(candidate.Revision).AsTask();
        NavigationHostIncident replacement = new(new NavigationIncidentId(Guid.NewGuid()), "A separate root equipment loss was reported.", new[] { additionalMember });
        Task<HostIncidentReportResult> reportTask = host.HostIncidents.ReportAsync(replacement).AsTask();

        Assert.Equal(incident.Id, candidate.HostIncident?.Id);
        realizer.ReleaseBlockedCompletion();

        NavigationState handoff = await handoffObserved;
        HostIncidentReportResult report = await reportTask;
        NavigationResult recovery = await recoveryTask;

        Assert.Equal(HostIncidentReportResult.Applied, report);
        Assert.Equal(replacement.Id, handoff.HostIncident?.Id);
        Assert.Contains(additionalMember, handoff.HostIncident!.RequiredPresentations);
        Assert.Equal(replacement.Id, recovery.FinalSnapshot.HostIncident?.Id);
        Assert.Equal(handoff.Revision, recovery.FinalSnapshot.Revision);
        Assert.NotEqual(incident.Id, recovery.FinalSnapshot.HostIncident?.Id);
    }

    [Fact]
    public async Task A_root_or_world_host_incident_with_no_required_presentation_is_resolved_once_and_rejects_its_late_report ()
    {
        CoordinatedPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationHostIncident incident = new(new NavigationIncidentId(Guid.NewGuid()), "The world blocker was lost.", Array.Empty<PresentationReference>());

        HostIncidentReportResult first = await host.HostIncidents.ReportAsync(incident);
        NavigationState incidentState = host.State.Current;
        NavigationResult recovery = await host.Recovery.RecoverAsync(incident.Id);
        long revisionAfterRecovery = recovery.FinalSnapshot.Revision;
        HostIncidentReportResult late = await host.HostIncidents.ReportAsync(incident);

        Assert.Equal(HostIncidentReportResult.Applied, first);
        Assert.Equal(incident.Id, incidentState.HostIncident?.Id);
        Assert.Empty(incidentState.HostIncident!.RequiredPresentations);
        Assert.Equal(NavigationResultKind.Committed, recovery.Kind);
        Assert.Null(recovery.FinalSnapshot.HostIncident);
        Assert.Equal(HostIncidentReportResult.Obsolete, late);
        Assert.Equal(revisionAfterRecovery, host.State.Current.Revision);
    }

    private static async Task<NavigationHostIncident> ReportHostIncidentForCurrentMainAsync (NavigationHost host)
    {
        NavigationResult reset = await host.Client.Reset(host.Root, MainDestination()).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        PresentationReference member = new(main.Id, reset.FinalSnapshot.GetPresentation(main.Id).Id!.Value);
        NavigationHostIncident incident = new(new NavigationIncidentId(Guid.NewGuid()), "The root equipment was lost.", new[] { member });

        Assert.Equal(HostIncidentReportResult.Applied, await host.HostIncidents.ReportAsync(incident));
        return incident;
    }

    private static async Task<(NavigationHostIncident Incident, PresentationReference AdditionalMember)> ReportHostIncidentForMainWithChildAsync (NavigationHost host)
    {
        NavigationResult reset = await host.Client.Reset(host.Root, MainDestination()).WaitAsync();
        NavigationState state = reset.FinalSnapshot;
        NavigationEntry main = state.GetEntry(state.GetRegion(host.Root).Entries.Single());
        NavigationEntry home = state.GetEntry(state.GetRegion(FindChildRegion(state, main.Id, Tabs)).Entries.Single());
        PresentationReference mainMember = new(main.Id, state.GetPresentation(main.Id).Id!.Value);
        PresentationReference homeMember = new(home.Id, state.GetPresentation(home.Id).Id!.Value);
        NavigationHostIncident incident = new(new NavigationIncidentId(Guid.NewGuid()), "The root equipment was lost.", new[] { mainMember });

        Assert.Equal(HostIncidentReportResult.Applied, await host.HostIncidents.ReportAsync(incident));
        return (incident, homeMember);
    }

    private static async Task<NavigationHostIncident> ReportRootHostIncidentAsync (NavigationHost host)
    {
        NavigationHostIncident incident = new(new NavigationIncidentId(Guid.NewGuid()), "The world blocker was lost.", Array.Empty<PresentationReference>());

        Assert.Equal(HostIncidentReportResult.Applied, await host.HostIncidents.ReportAsync(incident));
        return incident;
    }

    private static NavigationDestinationTree<MainRoute> MainDestination () => Destination.For(new MainRoute()).Child(Tabs, Destination.For(new HomeRoute()));

    private static void AssertRetainedChildChange (PresentationUpdate update, NavigationEntry parent, PresentationId parentPresentation, RegionDefinitionId expectedDefinition, RegionInstanceId expectedInstance, IReadOnlyList<NavigationEntryId> expectedBefore, IReadOnlyList<NavigationEntryId> expectedAfter, Type? expectedAfterRoute)
    {
        PresentationChangeContext context = Assert.Single(update.RetainedChanges, change => change.Self.BeforeEntry?.Id == parent.Id);
        ChildRegionChange child = Assert.Single(context.ChildChanges);

        Assert.Equal(parent.Id, context.Self.BeforeEntry!.Id);
        Assert.Equal(parent.Id, context.Self.AfterEntry!.Id);
        Assert.Equal(parentPresentation, context.Self.Before!.Id);
        Assert.Equal(parentPresentation, context.Self.After!.Id);
        Assert.Equal(expectedDefinition, child.DefinitionId);
        Assert.Equal(expectedInstance, child.InstanceId);
        Assert.Single(update.RetainedChanges.SelectMany(change => change.ChildChanges));
        Assert.Equal(expectedBefore, child.BeforeEntries.Select(entry => entry.Entry.Id));
        Assert.Equal(expectedAfter, child.AfterEntries.Select(entry => entry.Entry.Id));
        Assert.All(child.BeforeEntries.Concat(child.AfterEntries), entry => Assert.True(entry.Participation.Available));
        if (expectedAfterRoute is null)
        {
            Assert.Empty(child.AfterEntries);
        }
        else
        {
            Assert.IsType(expectedAfterRoute, Assert.Single(child.AfterEntries).Entry.Route);
        }
    }

    private static void AssertComposition (EffectiveComposition composition, IEnumerable<(NavigationEntryId EntryId, PresentationParticipation Participation)> expected, IEnumerable<InputBoundary> inputBoundaries)
    {
        (NavigationEntryId EntryId, PresentationParticipation Participation)[] expectedItems = expected.ToArray();

        Assert.Equal(expectedItems.Select(item => item.EntryId), composition.Presentations.Select(presentation => presentation.EntryId));
        Assert.Equal(expectedItems.Select(item => item.Participation), composition.Presentations.Select(presentation => presentation.Participation));
        Assert.Equal(inputBoundaries, composition.InputBoundaries);
    }

    private static RegionInstanceId FindChildRegion (NavigationState state, NavigationEntryId owner, RegionDefinitionId definitionId)
    {
        return state.Regions.Values.Single(region => region.OwnerEntryId == owner && region.DefinitionId == definitionId).Id;
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                main.AddChildRegion(Tabs, RegionCompositionMode.Exclusive, RegionOccupancy.Required, tabs =>
                {
                    tabs.AddRoute<HomeRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Replace;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    });
                    tabs.AddRoute<SettingsRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Replace;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    });
                });
                main.AddChildRegion(Notices, RegionCompositionMode.Layered, RegionOccupancy.Optional, notices =>
                    notices.AddRoute<NoticeRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Push;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    }));
                main.AddChildRegion(Sibling, RegionCompositionMode.Layered, RegionOccupancy.Optional, sibling =>
                    sibling.AddRoute<SiblingRoute>(route =>
                    {
                        route.AllowedEntryOperations = RouteEntryOperations.Push;
                        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    }));
            });
            root.AddRoute<OverlayRoute>(overlay =>
            {
                overlay.AllowedEntryOperations = RouteEntryOperations.Push;
                overlay.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRelease;
            });
        });
    }

    private sealed record MainRoute : Route;
    private sealed record HomeRoute : Route;
    private sealed record SettingsRoute : Route;
    private sealed record NoticeRoute : Route;
    private sealed record SiblingRoute : Route;
    private sealed record OverlayRoute : Route;

}
