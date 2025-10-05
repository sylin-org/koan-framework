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

    [Fact]
    public void State_SetterShouldCloneIncomingInstance()
    {
        var metadata = new CanonMetadata();
        var state = CanonState.Default.WithSignal("flag", "on");

        metadata.State = state;

        ReferenceEquals(metadata.State, state).Should().BeFalse();
        metadata.State.Signals.Should().ContainKey("flag");
    }

    [Fact]
    public void Merge_ShouldCombineSignalsWhenPreferringExisting()
    {
        var primary = new CanonMetadata();
        primary.State = primary.State.WithSignal("alpha", "1");

        var incoming = new CanonMetadata();
        incoming.State = incoming.State.WithSignal("beta", "2");

        primary.Merge(incoming, preferIncoming: false);

        primary.State.Lifecycle.Should().Be(CanonLifecycle.Active);
    primary.State.Signals.Should().ContainKeys("alpha", "beta");
    }

    [Fact]
    public void Merge_ShouldPreferIncomingStateWhenRequested()
    {
        var primary = new CanonMetadata();
        primary.State = primary.State.WithLifecycle(CanonLifecycle.PendingRetirement);

        var incoming = new CanonMetadata();
        var incomingState = incoming.State.WithLifecycle(CanonLifecycle.Superseded).WithSignal("beta", "2");
        incoming.State = incomingState;

        primary.Merge(incoming, preferIncoming: true);

        primary.State.Lifecycle.Should().Be(CanonLifecycle.Superseded);
        primary.State.Signals.Should().ContainKey("beta");
        ReferenceEquals(primary.State, incomingState).Should().BeFalse();
    }
}
