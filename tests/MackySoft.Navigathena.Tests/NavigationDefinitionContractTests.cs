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
            root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional,
                    child => child.AddRoute<ChildRoute>(DefinePushRoute));
            });
            root.AddRoute<SecondRootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
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
            Assert.Throws<InvalidOperationException>(() => root.AddRoute<SecondRootRoute>(route =>
            {
                route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
                    child.AddRoute<ChildRoute>(childRoute =>
                    {
                        DefinePushRoute(childRoute);
                        childRoute.AddChildRegion(grandchild, RegionCompositionMode.Layered, RegionOccupancy.Optional,
                            region => region.AddRoute<ChildRoute>(DefinePushRoute));
                    }));
                throw new InvalidOperationException("Configuration failed.");
            }));
            root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional,
                    child => child.AddRoute<ChildRoute>(DefinePushRoute));
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
            root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                Action<ChildRegionDefinitionBuilder> defineChild = child => child.AddRoute<ChildRoute>(childRoute =>
                {
                    DefinePushRoute(childRoute);
                    childRoute.AddChildRegion(grandchild, RegionCompositionMode.Layered, RegionOccupancy.Optional,
                        region => region.AddRoute<ChildRoute>(DefinePushRoute));
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
            root => root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            })));
    }

    [Fact]
    public void Build_requires_each_route_to_declare_its_entry_operations_and_lower_presentation_policy ()
    {
        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root => root.AddRoute<RootRoute>(route => route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve)));

        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root => root.AddRoute<RootRoute>(route => route.AllowedEntryOperations = RouteEntryOperations.Reset)));
    }

    [Fact]
    public void Route_configuration_can_read_and_override_settings_before_the_definition_is_built ()
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;

                Assert.Equal(RouteEntryOperations.Reset, route.AllowedEntryOperations);
                Assert.Equal(LowerPresentationPolicy.Preserve, route.LowerPresentationPolicy);

                route.AllowedEntryOperations |= RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRelease;
            }));

        RegionRouteDefinition registration = Assert.Single(definition.GetRegion(Root).Routes);
        Assert.Equal(RouteEntryOperations.Reset | RouteEntryOperations.Push, registration.AllowedEntryOperations);
        Assert.Equal(LowerPresentationPolicy.HideAndRelease, registration.LowerPresentationPolicy);
    }

    [Fact]
    public void Unconfigured_route_settings_cannot_be_read_as_defaults ()
    {
        NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<RootRoute>(route =>
            {
                Assert.Throws<InvalidOperationException>(() => route.AllowedEntryOperations);
                Assert.Throws<InvalidOperationException>(() => route.LowerPresentationPolicy);

                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            }));
    }

    [Theory]
    [InlineData(RouteEntryOperations.None)]
    [InlineData((RouteEntryOperations)8)]
    [InlineData(RouteEntryOperations.Reset | (RouteEntryOperations)8)]
    public void Invalid_entry_operations_are_rejected_without_replacing_the_configured_permissions (RouteEntryOperations invalidOperations)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                Assert.Throws<NavigationConfigurationException>(() => route.AllowedEntryOperations = invalidOperations);
            }));

        Assert.Equal(RouteEntryOperations.Reset, Assert.Single(definition.GetRegion(Root).Routes).AllowedEntryOperations);
    }

    [Theory]
    [InlineData(LowerPresentationOutput.Preserve, LowerPresentationInput.Block, LowerPresentationRetention.Release)]
    [InlineData(LowerPresentationOutput.Hide, LowerPresentationInput.PassThrough, LowerPresentationRetention.Release)]
    [InlineData((LowerPresentationOutput)2, LowerPresentationInput.Block, LowerPresentationRetention.Retain)]
    [InlineData(LowerPresentationOutput.Hide, (LowerPresentationInput)2, LowerPresentationRetention.Retain)]
    [InlineData(LowerPresentationOutput.Hide, LowerPresentationInput.Block, (LowerPresentationRetention)2)]
    public void Invalid_lower_presentation_policy_is_rejected_without_replacing_the_configured_policy (
        LowerPresentationOutput output, LowerPresentationInput input, LowerPresentationRetention retention)
    {
        NavigationDefinition definition = NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
            root.AddRoute<RootRoute>(route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                Assert.Throws<ArgumentException>(() => route.LowerPresentationPolicy = new LowerPresentationPolicy(output, input, retention));
                Assert.Throws<ArgumentNullException>(() => route.LowerPresentationPolicy = null!);
            }));

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

        NavigationDefinition Build () => NavigationDefinition.Build(Root, RegionCompositionMode.Layered, root =>
        {
            capturedRoot = root;
            root.AddRoute<RootRoute>(route =>
            {
                capturedRoute = route;
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                route.AddChildRegion(Child, RegionCompositionMode.Exclusive, RegionOccupancy.Optional, child =>
                {
                    capturedChild = child;
                    child.AddRoute<ChildRoute>(childRoute =>
                    {
                        capturedChildRoute = childRoute;
                        DefinePushRoute(childRoute);
                        if (failConfiguration)
                        {
                            throw failure;
                        }
                    });
                    Assert.Throws<InvalidOperationException>(() => capturedChildRoute!.AllowedEntryOperations = RouteEntryOperations.Reset);
                });
                Assert.Throws<InvalidOperationException>(() => capturedChild!.AddRoute<SecondRootRoute>(_ =>
{
}));
            });
            Assert.Throws<InvalidOperationException>(() => capturedRoute!.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRelease);
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
        Assert.Throws<InvalidOperationException>(() => capturedRoot.AddRoute<SecondRootRoute>(_ =>
{
}));
        Assert.Throws<InvalidOperationException>(() => capturedRoute.AllowedEntryOperations = RouteEntryOperations.Push);
        Assert.Throws<InvalidOperationException>(() => capturedRoute.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput);
        Assert.Throws<InvalidOperationException>(() => capturedRoute.AddChildRegion(new RegionDefinitionId("late"), RegionCompositionMode.Layered, RegionOccupancy.Optional, _ =>
{
}));
        Assert.Throws<InvalidOperationException>(() => capturedChild.AddRoute<SecondRootRoute>(_ =>
{
}));
        Assert.Throws<InvalidOperationException>(() => capturedChildRoute.AllowedEntryOperations = RouteEntryOperations.Reset);
    }

    [Fact]
    public void Build_rejects_duplicate_region_identifiers_and_duplicate_exact_route_types_in_one_region ()
    {
        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root =>
            {
                root.AddRoute<RootRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    route.AddChildRegion(Child, RegionCompositionMode.Exclusive, RegionOccupancy.Optional, child =>
                    {
                        child.AddRoute<ChildRoute>(DefinePushRoute);
                    });
                });
                root.AddRoute<SecondRootRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    route.AddChildRegion(Child, RegionCompositionMode.Layered, RegionOccupancy.Optional, child =>
                    {
                        child.AddRoute<ChildRoute>(DefinePushRoute);
                    });
                });
            }));

        Assert.Throws<NavigationConfigurationException>(() => NavigationDefinition.Build(
            Root,
            RegionCompositionMode.Exclusive,
            root =>
            {
                root.AddRoute<RootRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                });
                root.AddRoute<RootRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                });
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
                root.AddRoute<SharedRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                });
                root.AddRoute<RootRoute>(route =>
                {
                    route.AllowedEntryOperations = RouteEntryOperations.Reset;
                    route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                    route.AddChildRegion(Child, RegionCompositionMode.Exclusive, RegionOccupancy.Optional, child =>
                        child.AddRoute<SharedRoute>(shared =>
                        {
                            shared.AllowedEntryOperations = RouteEntryOperations.Push;
                            shared.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
                        }));
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
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Preserve, LowerPresentationInput.PassThrough, LowerPresentationRetention.Retain), LowerPresentationPolicy.Preserve);
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Preserve, LowerPresentationInput.Block, LowerPresentationRetention.Retain), LowerPresentationPolicy.BlockInput);
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Hide, LowerPresentationInput.Block, LowerPresentationRetention.Retain), LowerPresentationPolicy.HideAndRetain);
        Assert.Equal(new LowerPresentationPolicy(LowerPresentationOutput.Hide, LowerPresentationInput.Block, LowerPresentationRetention.Release), LowerPresentationPolicy.HideAndRelease);
    }

    private static void DefinePushRoute (RouteDefinitionBuilder<ChildRoute> route)
    {
        route.AllowedEntryOperations = RouteEntryOperations.Push;
        route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
    }

    private sealed record RootRoute : Route;
    private sealed record SecondRootRoute : Route;
    private sealed record ChildRoute : Route;
    private record SharedRoute : Route;
    private sealed record DerivedSharedRoute : SharedRoute;
}
