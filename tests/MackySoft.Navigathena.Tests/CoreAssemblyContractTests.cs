using System;
using System.Linq;
using MackySoft.Navigathena.Hosting;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class CoreAssemblyContractTests
{
    [Fact]
    public void The_portable_core_consumer_does_not_load_a_unity_assembly ()
    {
        string[] referencedAssemblies = typeof(NavigationHost).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Unity", StringComparison.Ordinal));
    }
}
