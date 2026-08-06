using Koan.Data.Vector.Connector.InMemory;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

public sealed class InMemoryVectorFloorSpec
{
    [Fact(DisplayName = "in-memory vector declares the automatic semantic floor")]
    public void Declares_automatic_floor()
    {
        using var factory = new InMemoryVectorAdapterFactory(Options.Create(new InMemoryVectorOptions()));

        Assert.True(factory.IsAutomaticFloor);
        Assert.Equal(-100, factory.GetType().GetCustomAttribute<Koan.Core.ProviderPriorityAttribute>()?.Priority);
    }
}
