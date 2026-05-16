using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Tests.Cache.Abstractions.Specs;

public class CacheInvalidationSpec
{
    [Fact]
    public void EvictKey_BuildsKeyOnlyMessage()
    {
        var node = Guid.NewGuid();
        var key = new CacheKey("Todo:_:abc-123");

        var msg = CacheInvalidation.EvictKey(key, node, region: "tenant-1");

        msg.Kind.Should().Be(CacheInvalidationKind.EvictKey);
        msg.Key.Should().Be(key);
        msg.Tags.Should().BeNull();
        msg.OriginNodeId.Should().Be(node);
        msg.Region.Should().Be("tenant-1");
    }

    [Fact]
    public void EvictByTag_BuildsTagOnlyMessage()
    {
        var node = Guid.NewGuid();
        var tags = new HashSet<string> { "Todo", "tenant:1" };

        var msg = CacheInvalidation.EvictByTag(tags, node);

        msg.Kind.Should().Be(CacheInvalidationKind.EvictByTag);
        msg.Key.Should().BeNull();
        msg.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public void EvictAll_BuildsKeylessTaglessMessage()
    {
        var node = Guid.NewGuid();

        var msg = CacheInvalidation.EvictAll(node, region: "tenant-2");

        msg.Kind.Should().Be(CacheInvalidationKind.EvictAll);
        msg.Key.Should().BeNull();
        msg.Tags.Should().BeNull();
        msg.Region.Should().Be("tenant-2");
    }

    [Fact]
    public void PublishedAtUtc_IsAlwaysUtc()
    {
        var msg = CacheInvalidation.EvictAll(Guid.NewGuid());

        msg.PublishedAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }
}
