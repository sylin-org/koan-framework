using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class EntityContextCacheBehaviorSpec
{
    [Fact]
    public void Current_CacheBehavior_is_null_outside_a_scope()
    {
        EntityContext.Current?.CacheBehavior.Should().BeNull();
    }

    [Fact]
    public void WithCacheBehavior_pushes_then_pops_on_dispose()
    {
        EntityContext.Current?.CacheBehavior.Should().BeNull();

        using (EntityContext.WithCacheBehavior(CacheBehavior.Refresh))
        {
            EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Refresh);
        }

        EntityContext.Current?.CacheBehavior.Should().BeNull();
    }

    [Fact]
    public void NoCache_shorthand_pushes_Bypass()
    {
        using (EntityContext.NoCache())
        {
            EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Bypass);
        }
    }

    [Fact]
    public void RefreshCache_shorthand_pushes_Refresh()
    {
        using (EntityContext.RefreshCache())
        {
            EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Refresh);
        }
    }

    [Fact]
    public void CacheBehavior_inherits_through_nested_With_calls_unless_overridden()
    {
        using (EntityContext.WithCacheBehavior(CacheBehavior.Bypass))
        {
            EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Bypass);

            // Pushing a partition scope should inherit Bypass from the parent.
            using (EntityContext.Partition("tenant-1"))
            {
                EntityContext.Current!.Partition.Should().Be("tenant-1");
                EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Bypass,
                    "child scope must inherit ambient CacheBehavior from parent");
            }

            EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Bypass);
        }
    }

    [Fact]
    public void Nested_CacheBehavior_inner_overrides_outer()
    {
        using (EntityContext.WithCacheBehavior(CacheBehavior.Bypass))
        {
            using (EntityContext.WithCacheBehavior(CacheBehavior.Refresh))
            {
                EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Refresh);
            }

            EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Bypass,
                "outer scope must be restored after inner disposes");
        }
    }

    [Fact]
    public void Disposing_out_of_order_still_works()
    {
        // Spurious dispose order tolerance — Dispose() is idempotent in the underlying scope.
        var outer = EntityContext.WithCacheBehavior(CacheBehavior.Bypass);
        var inner = EntityContext.WithCacheBehavior(CacheBehavior.Refresh);

        inner.Dispose();
        EntityContext.Current!.CacheBehavior.Should().Be(CacheBehavior.Bypass);

        outer.Dispose();
        EntityContext.Current?.CacheBehavior.Should().BeNull();
    }
}
