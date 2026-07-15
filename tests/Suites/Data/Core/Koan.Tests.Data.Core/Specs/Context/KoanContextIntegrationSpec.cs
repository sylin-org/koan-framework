using AwesomeAssertions;
using Koan.Core.Context;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Context;

/// <summary>
/// R07-01: Data retains its Entity operation facade while generic logical-flow state belongs to Core. A Data routing
/// scope changes only <see cref="EntityContext.ContextState"/> and cannot copy, clear, or otherwise own another axis.
/// </summary>
public sealed class KoanContextIntegrationSpec
{
    private sealed record BusinessContext(string Value);

    [Fact]
    public void Entity_routing_scope_does_not_clobber_Core_context()
    {
        using (KoanContext.Push(new BusinessContext("outer")))
        using (EntityContext.With(partition: "p"))
        {
            KoanContext.Get<BusinessContext>().Should().Be(new BusinessContext("outer"));
            EntityContext.Current!.Partition.Should().Be("p");
        }

        KoanContext.Get<BusinessContext>().Should().BeNull();
        EntityContext.Current.Should().BeNull();
    }

    [Fact]
    public void Suppressing_another_axis_does_not_clear_Data_routing_state()
    {
        using (KoanContext.Push(new BusinessContext("outer")))
        using (EntityContext.With(partition: "p"))
        using (KoanContext.Suppress<BusinessContext>())
        {
            KoanContext.Get<BusinessContext>().Should().BeNull();
            EntityContext.Current!.Partition.Should().Be("p");
        }
    }
}
