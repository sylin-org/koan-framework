namespace Koan.Tests.Canon.Unit.Specs.Metadata;

public sealed class CanonMetadataSpec
{
    [Fact]
    public void External_id_updates_are_idempotent()
    {
        var metadata = new CanonMetadata();

        var first = metadata.RecordExternalId("crm", "123", source: "alpha");
        first.Value.Should().Be("123");
        first.Source.Should().Be("alpha");

        var second = metadata.RecordExternalId("crm", "456", source: "beta", observedAt: DateTimeOffset.UtcNow.AddMinutes(1));
        second.Value.Should().Be("456");
        second.Source.Should().Be("beta");

        metadata.TryGetExternalId("crm", out var current).Should().BeTrue();
        current!.Value.Should().Be("456");

        metadata.RemoveExternalId("crm").Should().BeTrue();
        metadata.TryGetExternalId("crm", out _).Should().BeFalse();
    }

    [Fact]
    public void Merge_prefers_existing_when_configured()
    {
        var baseline = new CanonMetadata();
        baseline.AssignCanonicalId("base-1");
        baseline.SetOrigin("orig-A");
        baseline.RecordExternalId("crm", "123");
        baseline.SetTag("env", "dev");

        var incoming = new CanonMetadata();
        incoming.AssignCanonicalId("incoming-9");
        incoming.SetOrigin("orig-B");
        incoming.RecordExternalId("crm", "789");
        incoming.SetTag("release", "r2");

        baseline.Merge(incoming, preferIncoming: false);

        baseline.HasCanonicalId.Should().BeTrue();
        baseline.TryGetExternalId("crm", out var external).Should().BeTrue();
        external!.Value.Should().Be("123");
        baseline.TryGetTag("env", out var env).Should().BeTrue();
        env.Should().Be("dev");
        baseline.TryGetTag("release", out var release).Should().BeTrue();
        release.Should().Be("r2");

        baseline.Merge(incoming);
        baseline.TryGetExternalId("crm", out external).Should().BeTrue();
        external!.Value.Should().Be("789");
    }

    [Fact]
    public void Record_source_updates_timestamp()
    {
        var metadata = new CanonMetadata();
        var before = DateTimeOffset.UtcNow;

        var source = metadata.RecordSource("adapter:alpha", attribution =>
        {
            attribution.DisplayName = "Adapter Alpha";
            attribution.SetAttribute("region", "east");
        });

        source.Key.Should().Be("adapter:alpha");
        source.DisplayName.Should().Be("Adapter Alpha");
        source.Attributes.Should().ContainKey("region").WhoseValue.Should().Be("east");
        source.SeenAt.Should().BeOnOrAfter(before);

        var updated = metadata.RecordSource("adapter:alpha", attribution =>
        {
            attribution.SetAttribute("environment", "qa");
        });

        updated.SeenAt.Should().BeOnOrAfter(source.SeenAt);
        updated.Attributes.Should().ContainKey("environment").WhoseValue.Should().Be("qa");
        updated.Attributes.Should().ContainKey("region").WhoseValue.Should().Be("east");
    }
}
