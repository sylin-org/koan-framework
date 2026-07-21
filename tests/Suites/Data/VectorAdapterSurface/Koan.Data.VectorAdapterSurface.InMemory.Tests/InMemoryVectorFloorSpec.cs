using Koan.Data.Vector.Connector.InMemory;
using System.Reflection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

public sealed class InMemoryVectorFloorSpec
{
    [Fact(DisplayName = "in-memory vector declares the automatic semantic floor")]
    public void Declares_automatic_floor()
    {
        var factory = new InMemoryVectorAdapterFactory();

        Assert.True(factory.IsAutomaticFloor);
        Assert.Equal(-100, factory.GetType().GetCustomAttribute<Koan.Core.ProviderPriorityAttribute>()?.Priority);
    }
}
