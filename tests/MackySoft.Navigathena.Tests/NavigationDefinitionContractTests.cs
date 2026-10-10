using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class NavigationDefinitionContractTests
{
    private static readonly RegionDefinitionId Root = new("root");
    private static readonly RegionDefinitionId Child = new("child");

    [Fact]
    public void Built_definition_collections_cannot_be_changed_through_mutable_collection_interfaces ()
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
{
    route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
});
            root.AddRoute<SecondRootRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve);
        });
        RegionDefinition root = definition.GetRegion(Root);
        RegionRouteDefinition route = root.Routes[0];
        RegionDefinition child = definition.GetRegion(Child);

        AssertCannotReplace(definition.Regions, child);
        AssertCannotReplace(root.Routes, root.Routes[1]);
        AssertCannotReplace(route.ChildRegions, root);

        Assert.Same(root, definition.GetRegion(Root));
        Assert.Same(route, root.Routes[0]);
        Assert.True(definition.TryGetRoute(Root, typeof(RootRoute), out RegionRouteDefinition? resolved));
        Assert.Same(route, resolved);
        Assert.Same(child, Assert.Single(route.ChildRegions));
        Assert.Equal(2, definition.Regions.Count);
    }

    [Fact]
    public void Failed_route_registration_discards_all_of_its_child_regions ()
    {
        RegionDefinitionId grandchild = new("grandchild");
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            Assert.Throws<InvalidOperationException>(() => root.AddRoute<SecondRootRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve, route =>
            {
                route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
                    child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve, childRoute =>
{
    childRoute.AddChildRegion(grandchild, RegionCompositionMode.Layered, RegionOccupancy.Optional, region => region.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
}));
                throw new InvalidOperationException("Configuration failed.");
            }));
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
{
    route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child => child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
});
        });

        Assert.Equal(2, definition.Regions.Count);
        Assert.False(definition.TryGetRoute(Root, typeof(SecondRootRoute), out _));
        RegionDefinition child = Assert.Single(Assert.Single(definition.GetRegion(Root).Routes).ChildRegions);
        Assert.Same(child, definition.GetRegion(Child));
        Assert.Empty(Assert.Single(child.Routes).ChildRegions);
        Assert.Throws<ArgumentException>(() => definition.GetRegion(grandchild));
    }

    [Fact]
    public void Failed_child_registration_does_not_reserve_its_descendant_identifiers ()
    {
        RegionDefinitionId grandchild = new("grandchild");
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
{
    Action<ChildRegionDefinitionBuilder> defineChild = child => child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve, childRoute =>
{
    childRoute.AddChildRegion(grandchild, RegionCompositionMode.Layered, RegionOccupancy.Optional, region => region.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
});
    Assert.Throws<InvalidOperationException>(() => route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
    {
        defineChild(child);
        throw new InvalidOperationException("Configuration failed.");
    }));
    route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, defineChild);
}));

        Assert.Equal(3, definition.Regions.Count);
        Assert.Equal(3, definition.Regions.Select(region => region.Id).Distinct().Count());
        RegionDefinition child = Assert.Single(Assert.Single(definition.GetRegion(Root).Routes).ChildRegions);
        Assert.Same(child, definition.GetRegion(Child));
        Assert.Same(definition.GetRegion(grandchild), Assert.Single(Assert.Single(child.Routes).ChildRegions));
    }

    private static void AssertCannotReplace<T> (IReadOnlyList<T> items, T replacement)
    {
        if (items is IList<T> mutable)
        {
            Assert.Throws<NotSupportedException>(() => mutable[0] = replacement);
        }
    }

    [Fact]
    public void Build_requires_a_root_route_that_permits_reset ()
    {
        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root => root.AddRoute<RootRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve)));
    }

    [Fact]
    public void Registration_records_the_supplied_entry_permissions_and_lower_policy ()
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.HideAndRelease));

        RegionRouteDefinition registration = Assert.Single(definition.GetRegion(Root).Routes);
        Assert.Equal(RouteEntryOperations.Reset | RouteEntryOperations.Push, registration.AllowedEntryOperations);
        Assert.Equal(LowerPresentationPolicy.HideAndRelease, registration.LowerPresentationPolicy);
    }

    [Theory]
    [InlineData(RouteEntryOperations.None)]
    [InlineData((RouteEntryOperations)8)]
    [InlineData(RouteEntryOperations.Reset | (RouteEntryOperations)8)]
    public void Invalid_entry_operations_do_not_reserve_the_route_type (RouteEntryOperations invalidOperations)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            Assert.Throws<NavigationConfigurationException>(() => root.AddRoute<RootRoute>(invalidOperations, LowerPresentationPolicy.Preserve));
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve);
        });

        Assert.Equal(RouteEntryOperations.Reset, Assert.Single(definition.GetRegion(Root).Routes).AllowedEntryOperations);
    }

    [Theory]
    [InlineData(LowerPresentationOutput.Preserve, LowerPresentationActivity.Suspend, LowerPresentationRetention.Release, LowerPresentationBoundary.Required)]
    [InlineData(LowerPresentationOutput.Hide, LowerPresentationActivity.Continue, LowerPresentationRetention.Release, LowerPresentationBoundary.Required)]
    [InlineData((LowerPresentationOutput)2, LowerPresentationActivity.Suspend, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required)]
    [InlineData(LowerPresentationOutput.Hide, (LowerPresentationActivity)2, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required)]
    [InlineData(LowerPresentationOutput.Hide, LowerPresentationActivity.Suspend, (LowerPresentationRetention)2, LowerPresentationBoundary.Required)]
    [InlineData(LowerPresentationOutput.Hide, LowerPresentationActivity.Suspend, LowerPresentationRetention.Retain, (LowerPresentationBoundary)2)]
    public void Invalid_lower_policy_does_not_reserve_the_route_type (
        LowerPresentationOutput output, LowerPresentationActivity activity, LowerPresentationRetention retention, LowerPresentationBoundary boundary)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            Assert.Throws<ArgumentException>(() => root.AddRoute<RootRoute>(RouteEntryOperations.Reset, new LowerPresentationPolicy(output, activity, retention, boundary)));
            Assert.Throws<ArgumentNullException>(() => root.AddRoute<RootRoute>(RouteEntryOperations.Reset, null!));
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve);
        });

        Assert.Equal(LowerPresentationPolicy.Preserve, Assert.Single(definition.GetRegion(Root).Routes).LowerPresentationPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Builders_reject_changes_after_their_configuration_callbacks_return_or_throw (bool failConfiguration)
    {
        RootRegionDefinitionBuilder? capturedRoot = null;
        RouteDefinitionBuilder<RootRoute>? capturedRoute = null;
        ChildRegionDefinitionBuilder? capturedChild = null;
        RouteDefinitionBuilder<ChildRoute>? capturedChildRoute = null;
        Exception failure = new("Configuration failed.");

        static void AddLateChild<TRoute> (RouteDefinitionBuilder<TRoute> route) where TRoute : NavigationRoute
        {
            route.AddChildRegion(new RegionDefinitionId("late"), RegionCompositionMode.Layered, RegionOccupancy.Optional,
                child => child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        }

        NavigationDefinition Build () => NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            capturedRoot = root;
            root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
            {
                capturedRoute = route;
                route.AddChildRegion(Child, RegionCompositionMode.Exclusive, RegionOccupancy.Optional, child =>
                {
                    capturedChild = child;
                    child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve, childRoute =>
                    {
                        capturedChildRoute = childRoute;
                        if (failConfiguration)
                        {
                            throw failure;
                        }
                    });
                    Assert.Throws<InvalidOperationException>(() => AddLateChild(capturedChildRoute!));
                });
                Assert.Throws<InvalidOperationException>(() => capturedChild!.AddRoute<SecondRootRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
            });
            Assert.Throws<InvalidOperationException>(() => AddLateChild(capturedRoute!));
        });

        if (failConfiguration)
        {
            Assert.Same(failure, Assert.Throws<Exception>(() => Build()));
        }
        else
        {
            Build();
        }

        Assert.NotNull(capturedRoot);
        Assert.NotNull(capturedRoute);
        Assert.NotNull(capturedChild);
        Assert.NotNull(capturedChildRoute);
        Assert.Throws<InvalidOperationException>(() => capturedRoot.AddRoute<SecondRootRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        Assert.Throws<InvalidOperationException>(() => AddLateChild(capturedRoute));
        Assert.Throws<InvalidOperationException>(() => capturedChild.AddRoute<SecondRootRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
        Assert.Throws<InvalidOperationException>(() => AddLateChild(capturedChildRoute));
    }

    [Fact]
    public void Build_rejects_duplicate_region_identifiers_and_duplicate_exact_route_types_in_one_region ()
    {
        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root =>
            {
                root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
{
    route.AddChildRegion(Child, RegionCompositionMode.Exclusive, RegionOccupancy.Optional, child =>
    {
        child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve);
    });
});
                root.AddRoute<SecondRootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
{
    route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
    {
        child.AddRoute<ChildRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve);
    });
});
            }));

        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root =>
            {
                root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve);
                root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve);
            }));
    }

    [Fact]
    public void Route_registration_uses_the_exact_route_type_and_is_scoped_to_its_region ()
    {
        NavigationDefinition definition = NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root =>
            {
                root.AddRoute<SharedRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve);
                root.AddRoute<RootRoute>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, route =>
{
    route.AddChildRegion(Child, RegionCompositionMode.Exclusive, RegionOccupancy.Optional, child => child.AddRoute<SharedRoute>(RouteEntryOperations.Push, LowerPresentationPolicy.Preserve));
});
            });

        Assert.True(definition.TryGetRoute(Root, typeof(SharedRoute), out RegionRouteDefinition? rootRegistration));
        Assert.True(definition.TryGetRoute(Child, typeof(SharedRoute), out RegionRouteDefinition? childRegistration));
        Assert.NotEqual(rootRegistration!.Key, childRegistration!.Key);
        Assert.False(definition.TryGetRoute(Root, typeof(DerivedSharedRoute), out _));
    }

    [Fact]
    public void Built_in_lower_presentation_policies_express_the_documented_effects ()
    {
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Preserve, LowerPresentationActivity.Continue, LowerPresentationRetention.Retain, LowerPresentationBoundary.WhenLowerVisible), LowerPresentationPolicy.Preserve);
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Preserve, LowerPresentationActivity.Suspend, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required), LowerPresentationPolicy.SuspendActivity);
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Hide, LowerPresentationActivity.Suspend, LowerPresentationRetention.Retain, LowerPresentationBoundary.Required), LowerPresentationPolicy.HideAndRetain);
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Hide, LowerPresentationActivity.Suspend, LowerPresentationRetention.Release, LowerPresentationBoundary.Required), LowerPresentationPolicy.HideAndRelease);
    }

    private sealed record RootRoute : Route;
    private sealed record SecondRootRoute : Route;
    private sealed record ChildRoute : Route;
    private record SharedRoute : Route;
    private sealed record DerivedSharedRoute : SharedRoute;
}
