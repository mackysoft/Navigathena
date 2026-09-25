using System;
using System.Linq;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Presentation;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationHostContractTests
{
    private static readonly RegionDefinitionId Root = new("app");
    private static readonly RegionDefinitionId Tabs = new("tabs");
    private static readonly RegionDefinitionId Notices = new("notices");

    [Fact]
    public async Task Host_starts_with_an_empty_root_at_revision_zero_and_reset_publishes_a_new_snapshot ()
    {
        TestPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);

        NavigationState initial = host.State.Current;
        Task<NavigationState> change = host.State.WaitForChangeAsync(initial.Revision).AsTask();

        NavigationResult result = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationState observed = await change;

        Assert.Equal(0, initial.Revision);
        Assert.Empty(initial.GetRegion(initial.RootRegionInstanceId).Entries);
        Assert.Equal(NavigationResultKind.Committed, result.Kind);
        Assert.True(result.DestinationCommitted);
        Assert.Equal(1, observed.Revision);
        Assert.Same(observed, host.State.Current);
        Assert.NotEmpty(observed.GetRegion(observed.RootRegionInstanceId).Entries);
    }

    [Fact]
    public async Task Reset_with_a_required_initial_child_commits_the_parent_and_child_in_one_navigation_result ()
    {
        TestPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);

        NavigationResult result = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationState state = result.FinalSnapshot;
        NavigationEntry parent = state.GetEntry(state.GetRegion(state.RootRegionInstanceId).Entries.Single());
        RegionState tabs = state.Regions.Values.Single(region => region.OwnerEntryId == parent.Id && region.DefinitionId == Tabs);
        NavigationEntry child = state.GetEntry(tabs.Entries.Single());

        Assert.True(result.DestinationCommitted);
        Assert.Equal(NavigationResultKind.Committed, result.Kind);
        Assert.Contains(result.Changes.CreatedEntries, entry => entry.Id == parent.Id);
        Assert.Contains(result.Changes.CreatedEntries, entry => entry.Id == child.Id);
        Assert.IsType<MainRoute>(parent.Route);
        Assert.IsType<LobbyRoute>(child.Route);
    }

    [Fact]
    public async Task Invalid_initial_child_configuration_is_rejected_before_the_adapter_starts_preparation ()
    {
        TestPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);

        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute())));
        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute()).Child(new RegionDefinitionId("unknown"), Destination.For(new NoticeRoute()))));
        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await host.Client.ResetAsync(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new NoticeRoute()))));






        Assert.Equal(0, realizer.PrepareCount);
        Assert.Equal(0, host.State.Current.Revision);
        Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
    }

    [Fact]
    public async Task Required_region_does_not_allow_back_to_remove_its_last_entry ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationEntry parent = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId tabs = reset.FinalSnapshot.Regions.Values.Single(region => region.OwnerEntryId == parent.Id && region.DefinitionId == Tabs).Id;

        NavigationResult result = await host.Client.Back(tabs).WaitAsync();

        Assert.Equal(NavigationResultKind.Rejected, result.Kind);
        Assert.False(result.DestinationCommitted);
        Assert.IsType<LobbyRoute>(result.FinalSnapshot.GetEntry(result.FinalSnapshot.GetRegion(tabs).Entries.Single()).Route);
    }

    [Fact]
    public async Task Replace_in_a_required_exclusive_tab_region_does_not_create_a_back_history ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationEntry parent = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId tabs = reset.FinalSnapshot.Regions.Values.Single(region => region.OwnerEntryId == parent.Id && region.DefinitionId == Tabs).Id;

        NavigationResult replaced = await host.Client.Replace(tabs, Destination.For(new InventoryRoute())).WaitAsync();
        NavigationResult back = await host.Client.Back(tabs).WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, replaced.Kind);
        Assert.IsType<InventoryRoute>(replaced.FinalSnapshot.GetEntry(replaced.FinalSnapshot.GetRegion(tabs).Entries.Single()).Route);
        Assert.Equal(NavigationResultKind.Rejected, back.Kind);
        Assert.IsType<InventoryRoute>(back.FinalSnapshot.GetEntry(back.FinalSnapshot.GetRegion(tabs).Entries.Single()).Route);
    }

    [Fact]
    public async Task Optional_child_region_can_be_cleared_without_removing_its_owner ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationEntry parent = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId notices = reset.FinalSnapshot.Regions.Values.Single(region => region.OwnerEntryId == parent.Id && region.DefinitionId == Notices).Id;

        NavigationResult pushed = await host.Client.Push(notices, Destination.For(new NoticeRoute())).WaitAsync();
        NavigationResult cleared = await host.Client.Clear(notices).WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, pushed.Kind);
        Assert.Equal(NavigationResultKind.Committed, cleared.Kind);
        Assert.Empty(cleared.FinalSnapshot.GetRegion(notices).Entries);
        Assert.Contains(parent.Id, cleared.FinalSnapshot.Entries.Keys);
    }

    [Fact]
    public async Task Adapter_conflict_preserves_the_last_committed_navigation_state ()
    {
        TestPresentationRealizer realizer = new()
        {
            RejectCommit = true
        };
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);

        NavigationResult result = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();

        Assert.Equal(NavigationResultKind.Conflict, result.Kind);
        Assert.False(result.DestinationCommitted);
        Assert.Equal(0, host.State.Current.Revision);
        Assert.Empty(host.State.Current.GetRegion(host.Root).Entries);
    }

    [Fact]
    public async Task Client_rejects_an_entry_operation_not_permitted_by_the_target_route_before_adapter_preparation ()
    {
        TestPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationEntry parent = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        RegionInstanceId tabs = reset.FinalSnapshot.Regions.Values.Single(region => region.OwnerEntryId == parent.Id && region.DefinitionId == Tabs).Id;
        int preparationsBeforeRejectedRequest = realizer.PrepareCount;

        await Assert.ThrowsAsync<NavigationConfigurationException>(async () => await host.Client.PushAsync(tabs, Destination.For(new InventoryRoute())));


        Assert.Equal(preparationsBeforeRejectedRequest, realizer.PrepareCount);
        Assert.IsType<LobbyRoute>(host.State.Current.GetEntry(host.State.Current.GetRegion(tabs).Entries.Single()).Route);
    }

    [Fact]
    public async Task Source_bound_action_uses_its_current_presentation_and_rejects_it_after_that_presentation_is_removed ()
    {
        TestPresentationRealizer realizer = new();
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), realizer);
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        PresentationContext mainContext = Assert.Single(realizer.Contexts, context => context.EntryId == main.Id);
        IScreenNavigation openNotice = mainContext.Navigation.GetRegionNavigation(RegionTarget.Child(Notices));

        NavigationResult opened = await openNotice.Push(Destination.For(new NoticeRoute())).WaitAsync();
        NavigationResult replacedRoot = await host.Client.Reset(host.Root, Destination.For(new TitleRoute())).WaitAsync();
        NavigationResult stale = await openNotice.Push(Destination.For(new NoticeRoute())).WaitAsync();

        Assert.Equal(NavigationResultKind.Committed, opened.Kind);
        Assert.Equal(NavigationResultKind.Committed, replacedRoot.Kind);
        Assert.Equal(NavigationResultKind.Rejected, stale.Kind);
        Assert.False(stale.DestinationCommitted);
    }

    [Fact]
    public async Task Matching_presentation_loss_is_recorded_once_and_recovery_restores_that_current_loss ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());
        NavigationResult reset = await host.Client.Reset(host.Root, Destination.For(new MainRoute()).Child(Tabs, Destination.For(new LobbyRoute()))).WaitAsync();
        NavigationEntry main = reset.FinalSnapshot.GetEntry(reset.FinalSnapshot.GetRegion(host.Root).Entries.Single());
        PresentationId presentation = reset.FinalSnapshot.GetPresentation(main.Id).Id!.Value;
        PresentationLoss loss = new(new[] { new PresentationReference(main.Id, presentation) }, "The presentation was lost.");

        PresentationLossResult reported = await host.Loss.ReportAsync(loss);
        NavigationState lost = host.State.Current;
        NavigationIncidentId incident = lost.GetPresentation(main.Id).IncidentId!.Value;
        PresentationLossResult staleReport = await host.Loss.ReportAsync(loss);
        NavigationResult recovery = await host.Recovery.RecoverAsync(incident);

        Assert.Equal(PresentationLossResult.Applied, reported);
        Assert.Equal(PresentationMaterialization.Lost, lost.GetPresentation(main.Id).Materialization);
        Assert.Equal(PresentationLossResult.Obsolete, staleReport);
        Assert.Equal(NavigationResultKind.Committed, recovery.Kind);
        Assert.Equal(PresentationMaterialization.Available, recovery.FinalSnapshot.GetPresentation(main.Id).Materialization);
        Assert.Null(recovery.FinalSnapshot.GetPresentation(main.Id).IncidentId);
    }

    [Fact]
    public async Task Recovery_rejects_a_loss_incident_that_is_no_longer_current ()
    {
        await using NavigationHost host = NavigationHost.Create(CreateDefinition(), new TestPresentationRealizer());

        NavigationResult result = await host.Recovery.RecoverAsync(new NavigationIncidentId(Guid.NewGuid()));

        Assert.Equal(NavigationResultKind.Rejected, result.Kind);
        Assert.False(result.DestinationCommitted);
        Assert.Equal(0, host.State.Current.Revision);
    }

    private static NavigationDefinition CreateDefinition ()
    {
        return NavigationDefinition.Build(Root, RegionCompositionMode.Exclusive, root =>
        {
            root.AddRoute<TitleRoute>(title =>
            {
                title.AllowedEntryOperations = RouteEntryOperations.Reset;
                title.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            root.AddRoute<MainRoute>(main =>
            {
                main.AllowedEntryOperations = RouteEntryOperations.Reset;
                main.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                main.AddChildRegion(Tabs, RegionCompositionMode.Exclusive, RegionOccupancy.Required, tabs =>
                {
                    tabs.AddRoute<LobbyRoute>(lobby =>
                    {
                        lobby.AllowedEntryOperations = RouteEntryOperations.Replace;
                        lobby.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    });
                    tabs.AddRoute<InventoryRoute>(inventory =>
                    {
                        inventory.AllowedEntryOperations = RouteEntryOperations.Replace;
                        inventory.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    });
                });
                main.AddChildRegion(Notices, RegionCompositionMode.Layered, RegionOccupancy.Optional, notices =>
                {
                    notices.AddRoute<NoticeRoute>(notice =>
                    {
                        notice.AllowedEntryOperations = RouteEntryOperations.Push;
                        notice.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
                    });
                });
            });
        });
    }

    private sealed record TitleRoute : Route;
    private sealed record MainRoute : Route;
    private sealed record LobbyRoute : Route;
    private sealed record InventoryRoute : Route;
    private sealed record NoticeRoute : Route;
}
