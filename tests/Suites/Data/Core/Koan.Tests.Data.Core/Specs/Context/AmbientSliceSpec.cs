using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Context;

/// <summary>
/// ARCH-0097: the axis-generic ambient carrier. Cross-cutting axes (tenant, classification) ride as registered
/// typed slices via <see cref="EntityContext.WithSlice{T}"/>/<see cref="EntityContext.GetSlice{T}"/> — the data
/// core stays agnostic. Pins push/read, type-coexistence, inherit-across-<c>With</c>, restore, clear, and
/// async-parallel isolation.
/// </summary>
public class AmbientSliceSpec
{
    private sealed record Foo(string V);
    private sealed record Bar(int N);

    [Fact]
    public void GetSlice_is_null_when_absent()
        => EntityContext.GetSlice<Foo>().Should().BeNull();

    [Fact]
    public void WithSlice_pushes_then_restores()
    {
        EntityContext.GetSlice<Foo>().Should().BeNull();
        using (EntityContext.WithSlice(new Foo("a")))
            EntityContext.GetSlice<Foo>()!.V.Should().Be("a");
        EntityContext.GetSlice<Foo>().Should().BeNull();
    }

    [Fact]
    public void Slices_of_different_types_coexist()
    {
        using (EntityContext.WithSlice(new Foo("a")))
        using (EntityContext.WithSlice(new Bar(7)))
        {
            EntityContext.GetSlice<Foo>()!.V.Should().Be("a");
            EntityContext.GetSlice<Bar>()!.N.Should().Be(7);
        }
    }

    [Fact]
    public void Slice_inherits_across_a_routing_With()
    {
        using (EntityContext.WithSlice(new Foo("a")))
        using (EntityContext.With(partition: "p"))
        {
            EntityContext.GetSlice<Foo>()!.V.Should().Be("a");      // carried through a routing scope
            EntityContext.Current!.Partition.Should().Be("p");
        }
    }

    [Fact]
    public void Nested_WithSlice_overrides_then_restores()
    {
        using (EntityContext.WithSlice(new Foo("outer")))
        {
            using (EntityContext.WithSlice(new Foo("inner")))
                EntityContext.GetSlice<Foo>()!.V.Should().Be("inner");
            EntityContext.GetSlice<Foo>()!.V.Should().Be("outer");
        }
    }

    [Fact]
    public void WithSlice_null_clears_then_restores()
    {
        using (EntityContext.WithSlice(new Foo("a")))
        {
            using (EntityContext.WithSlice<Foo>(null))
                EntityContext.GetSlice<Foo>().Should().BeNull();
            EntityContext.GetSlice<Foo>()!.V.Should().Be("a");
        }
    }

    [Fact]
    public async Task Slices_do_not_clobber_across_parallel_async_flows()
    {
        async Task<string?> Scope(string v)
        {
            using (EntityContext.WithSlice(new Foo(v)))
            {
                await Task.Yield();
                return EntityContext.GetSlice<Foo>()?.V;
            }
        }

        var results = await Task.WhenAll(Scope("a"), Scope("b"), Scope("c"));
        results.OrderBy(x => x).Should().Equal("a", "b", "c");
    }
}
