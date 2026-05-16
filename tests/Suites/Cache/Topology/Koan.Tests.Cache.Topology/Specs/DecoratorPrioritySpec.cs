using System.Reflection;
using Koan.Cache.Decorators;
using Koan.Data.Abstractions;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class DecoratorPrioritySpec
{
    [Fact]
    public void CacheRepositoryDecorator_declares_ProviderPriority_100()
    {
        var attr = typeof(CacheRepositoryDecorator).GetCustomAttribute<ProviderPriorityAttribute>();

        attr.Should().NotBeNull("the cache decorator must declare its priority band to lock the cache-outer/CQRS-inner order");
        attr!.Priority.Should().Be(100, "100 = 'read short-circuit' band per ARCH-0076 (M10)");
    }
}
