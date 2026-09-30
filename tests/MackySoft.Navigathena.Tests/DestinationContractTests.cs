using System;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class DestinationContractTests
{
    private static readonly RegionDefinitionId Child = new("child");

    [Fact]
    public void Child_returns_a_new_destination_without_changing_the_original_destination ()
    {
        NavigationDestinationTree<ParentRoute> original = Destination.For(new ParentRoute());
        NavigationDestinationTree<ParentRoute> configured = original.Child(Child, Destination.For(new ChildRoute()));

        Assert.Empty(original.Children);
        Assert.Single(configured.Children);
        Assert.IsType<ChildRoute>(configured.Children[Child].Route);
    }

    [Fact]
    public void Child_rejects_a_second_initial_destination_for_the_same_region ()
    {
        NavigationDestinationTree<ParentRoute> destination = Destination.For(new ParentRoute())
            .Child(Child, Destination.For(new ChildRoute()));

        Assert.Throws<ArgumentException>(() => destination.Child(Child, Destination.For(new AlternateChildRoute())));
    }

    private sealed record ParentRoute : Route;
    private sealed record ChildRoute : Route;
    private sealed record AlternateChildRoute : Route;
}
