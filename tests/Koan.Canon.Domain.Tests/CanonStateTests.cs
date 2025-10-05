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
}
