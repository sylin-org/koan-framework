using FluentAssertions;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Model;
using Xunit;

namespace Koan.Canon.Domain.Tests;

public class CanonMetadataTests
{
    [Fact]
    public void RecordExternalId_ShouldStoreCaseInsensitive()
    {
        var metadata = new CanonMetadata();

        metadata.RecordExternalId("CRM", "123");

        metadata.TryGetExternalId("crm", out var externalId).Should().BeTrue();
        externalId!.Value.Should().Be("123");
    }

    [Fact]
    public void Clone_ShouldProduceIndependentCopies()
    {
        var metadata = new CanonMetadata();
        metadata.RecordExternalId("crm", "A1");
        metadata.SetTag("env", "dev");

        var clone = metadata.Clone();
        clone.RecordExternalId("erp", "B2");
        clone.SetTag("env", "prod");

        metadata.TryGetExternalId("erp", out _).Should().BeFalse();
        metadata.TryGetTag("env", out var tag).Should().BeTrue();
        tag.Should().Be("dev");
        clone.TryGetTag("env", out var cloneTag).Should().BeTrue();
        cloneTag.Should().Be("prod");
    }

    [Fact]
    public void Merge_ShouldRespectPreference()
    {
        var primary = new CanonMetadata();
        primary.RecordExternalId("crm", "123");
        primary.SetOrigin("primary");

        var secondary = new CanonMetadata();
        secondary.RecordExternalId("crm", "999");
        secondary.RecordExternalId("erp", "456");
        secondary.SetOrigin("secondary");

    primary.Merge(secondary, preferIncoming: false);

    primary.TryGetExternalId("crm", out var crm).Should().BeTrue();
    crm!.Value.Should().Be("123");
    primary.TryGetExternalId("erp", out var erp).Should().BeTrue();
    erp!.Value.Should().Be("456");
    primary.Origin.Should().Be("primary");
    }
}
