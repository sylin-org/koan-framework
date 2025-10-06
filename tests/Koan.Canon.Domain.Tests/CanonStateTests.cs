using FluentAssertions;
using Koan.Canon.Domain.Model;
using Xunit;

namespace Koan.Canon.Domain.Tests;

public class CanonStateTests
{
    [Fact]
    public void WithLifecycle_ShouldReturnNewInstance()
    {
        var original = CanonState.Default;

        var updated = original.WithLifecycle(CanonLifecycle.PendingRetirement);

        updated.Should().NotBeSameAs(original);
        updated.Lifecycle.Should().Be(CanonLifecycle.PendingRetirement);
        original.Lifecycle.Should().Be(CanonLifecycle.Active);
    }

    [Fact]
    public void WithSignal_ShouldAddAndRemoveSignals()
    {
        var state = CanonState.Default.WithSignal("alert", "pending");

        state.Signals.Should().ContainKey("alert");
        state.RequiresAttention.Should().BeTrue();

        var cleared = state.WithSignal("alert", null);

        cleared.Signals.Should().NotContainKey("alert");
    }

    [Fact]
    public void Copy_ShouldProduceIndependentInstance()
    {
        var state = CanonState.Default.WithSignal("flag", "on");

        var copy = state.Copy();

        copy.Should().NotBeSameAs(state);
        copy.Signals.Should().ContainKey("flag");

        var mutated = copy.WithSignal("flag", "off");

        state.Signals["flag"].Should().Be("on");
        mutated.Signals["flag"].Should().Be("off");
    }

    [Fact]
    public void Merge_WithPreferIncoming_ShouldAdoptIncomingLifecycle()
    {
        var current = CanonState.Default.WithLifecycle(CanonLifecycle.Archived);
        var incoming = CanonState.Default.WithLifecycle(CanonLifecycle.Withdrawn).WithSignal("reason", "policy");

        var merged = current.Merge(incoming, preferIncoming: true);

        merged.Lifecycle.Should().Be(CanonLifecycle.Withdrawn);
        merged.Signals.Should().ContainKey("reason");
        current.Lifecycle.Should().Be(CanonLifecycle.Archived);
    }

    [Fact]
    public void Merge_WithNullIncoming_ShouldReturnClone()
    {
        var current = CanonState.Default.WithSignal("note", "keep");

        var merged = current.Merge(null!, preferIncoming: false);

        merged.Should().NotBeSameAs(current);
        merged.Signals.Should().ContainKey("note");
    }

    [Fact]
    public void WithReadiness_ShouldUpdateRequiresAttention()
    {
        var state = CanonState.Default;

        state.RequiresAttention.Should().BeFalse();

        var degraded = state.WithReadiness(CanonReadiness.Degraded);

        degraded.RequiresAttention.Should().BeTrue();
        degraded.Readiness.Should().Be(CanonReadiness.Degraded);
        state.Readiness.Should().Be(CanonReadiness.Complete);
    }

    [Fact]
    public void WithSignal_ShouldUpdateTimestamp()
    {
        var state = CanonState.Default;
        var priorTimestamp = state.UpdatedAt;

        var mutated = state.WithSignal("alert", "yes");

        mutated.UpdatedAt.Should().BeAfter(priorTimestamp);
        mutated.Signals.Should().ContainKey("alert");
    }

    [Fact]
    public void Merge_WhenNotPreferringIncoming_ShouldRetainLifecycle()
    {
        var current = CanonState.Default.WithLifecycle(CanonLifecycle.Active).WithSignal("alpha", "1");
        var incoming = CanonState.Default.WithLifecycle(CanonLifecycle.Withdrawn).WithSignal("alpha", "override").WithSignal("beta", "2");

        var merged = current.Merge(incoming, preferIncoming: false);

        merged.Lifecycle.Should().Be(CanonLifecycle.Active);
        merged.Signals.Should().ContainKey("alpha").WhoseValue.Should().Be("1");
        merged.Signals.Should().ContainKey("beta").WhoseValue.Should().Be("2");
    }
}
