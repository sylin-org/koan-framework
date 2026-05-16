using System.Collections.Generic;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Coherence.Messaging.Channel;

namespace Koan.Tests.Cache.Coherence.Messaging.Specs;

public sealed class MessagingInvalidationEnvelopeSpec
{
    [Fact]
    public void RoundTrip_EvictKey_preserves_fields()
    {
        var node = Guid.NewGuid();
        var key = new CacheKey("Todo:_:abc-123");
        var original = CacheInvalidation.EvictKey(key, node, region: "tenant-1");

        var envelope = MessagingInvalidationEnvelope.FromMessage(original);
        var roundTripped = envelope.ToMessage();

        roundTripped.Kind.Should().Be(CacheInvalidationKind.EvictKey);
        roundTripped.Key.Should().Be(key);
        roundTripped.OriginNodeId.Should().Be(node);
        roundTripped.Region.Should().Be("tenant-1");
        roundTripped.Tags.Should().BeNull();
        // PublishedAtUtcTicks preserved
        envelope.PublishedAtUtcTicks.Should().Be(original.PublishedAtUtc.UtcTicks);
    }

    [Fact]
    public void RoundTrip_EvictByTag_preserves_tag_set()
    {
        var node = Guid.NewGuid();
        var tags = new HashSet<string> { "Todo", "tenant:1" };
        var original = CacheInvalidation.EvictByTag(tags, node);

        var envelope = MessagingInvalidationEnvelope.FromMessage(original);
        var roundTripped = envelope.ToMessage();

        roundTripped.Kind.Should().Be(CacheInvalidationKind.EvictByTag);
        roundTripped.Key.Should().BeNull();
        roundTripped.Tags.Should().BeEquivalentTo(tags);
    }

    [Fact]
    public void RoundTrip_EvictAll_preserves_minimal_fields()
    {
        var node = Guid.NewGuid();
        var original = CacheInvalidation.EvictAll(node, region: "shared");

        var envelope = MessagingInvalidationEnvelope.FromMessage(original);
        var roundTripped = envelope.ToMessage();

        roundTripped.Kind.Should().Be(CacheInvalidationKind.EvictAll);
        roundTripped.Key.Should().BeNull();
        roundTripped.Tags.Should().BeNull();
        roundTripped.Region.Should().Be("shared");
        roundTripped.OriginNodeId.Should().Be(node);
    }

    [Fact]
    public void Envelope_with_unknown_Kind_defaults_to_EvictKey()
    {
        // Defensive: a future producer might send a Kind value the consumer doesn't know.
        var envelope = new MessagingInvalidationEnvelope
        {
            Kind = "definitely-not-a-real-kind",
            Key = "foo",
            OriginNodeId = Guid.NewGuid().ToString("D"),
            PublishedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks,
        };

        var msg = envelope.ToMessage();

        msg.Kind.Should().Be(CacheInvalidationKind.EvictKey, "fail-soft to a known kind rather than throw");
    }

    [Fact]
    public void Envelope_with_unparseable_OriginNodeId_yields_Empty_guid()
    {
        var envelope = new MessagingInvalidationEnvelope
        {
            Kind = "EvictKey",
            Key = "foo",
            OriginNodeId = "not-a-guid",
            PublishedAtUtcTicks = 0,
        };

        var msg = envelope.ToMessage();

        msg.OriginNodeId.Should().Be(Guid.Empty);
    }
}
