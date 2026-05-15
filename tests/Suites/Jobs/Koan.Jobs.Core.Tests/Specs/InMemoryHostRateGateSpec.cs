using Koan.Jobs.RateGating;

namespace Koan.Jobs.Core.Tests.Specs;

public sealed class InMemoryHostRateGateSpec
{
    [Fact]
    public void TryGetGate_returns_false_when_no_gate_set()
    {
        var gate = new InMemoryHostRateGate();
        gate.TryGetGate("nexusmods", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GateHost_then_TryGetGate_returns_active_entry()
    {
        var gate = new InMemoryHostRateGate();
        await gate.GateHost("nexusmods", TimeSpan.FromMinutes(5), "429 received");

        gate.TryGetGate("nexusmods", out var entry).Should().BeTrue();
        entry.HostTag.Should().Be("nexusmods");
        entry.Reason.Should().Be("429 received");
        (entry.ReleaseAt - entry.SetAt).Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TryGetGate_is_case_insensitive_on_host_tag()
    {
        var gate = new InMemoryHostRateGate();
        await gate.GateHost("NexusMods", TimeSpan.FromMinutes(1), "test");
        gate.TryGetGate("nexusmods", out _).Should().BeTrue();
        gate.TryGetGate("NEXUSMODS", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Expired_gate_is_treated_as_not_gated()
    {
        var gate = new InMemoryHostRateGate();
        // Set a 1ms gate then wait it out.
        await gate.GateHost("nexusmods", TimeSpan.FromMilliseconds(1), "instant");
        await Task.Delay(20);
        gate.TryGetGate("nexusmods", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GateHost_extends_existing_gate_when_new_release_is_later()
    {
        var gate = new InMemoryHostRateGate();
        await gate.GateHost("nexusmods", TimeSpan.FromSeconds(2), "first");
        gate.TryGetGate("nexusmods", out var first).Should().BeTrue();
        var firstRelease = first.ReleaseAt;

        // Longer gate should extend the release.
        await gate.GateHost("nexusmods", TimeSpan.FromMinutes(10), "second");
        gate.TryGetGate("nexusmods", out var second).Should().BeTrue();
        second.ReleaseAt.Should().BeAfter(firstRelease);
        second.Reason.Should().Be("second");
    }

    [Fact]
    public async Task GateHost_keeps_existing_gate_when_new_release_is_earlier()
    {
        var gate = new InMemoryHostRateGate();
        await gate.GateHost("nexusmods", TimeSpan.FromMinutes(10), "long");
        gate.TryGetGate("nexusmods", out var first).Should().BeTrue();
        var longRelease = first.ReleaseAt;

        // Shorter gate should be a no-op — the longer one stays.
        await gate.GateHost("nexusmods", TimeSpan.FromSeconds(2), "short");
        gate.TryGetGate("nexusmods", out var second).Should().BeTrue();
        second.ReleaseAt.Should().Be(longRelease);
        second.Reason.Should().Be("long");
    }

    [Fact]
    public async Task ClearGate_removes_an_active_gate()
    {
        var gate = new InMemoryHostRateGate();
        await gate.GateHost("nexusmods", TimeSpan.FromMinutes(5), "test");
        gate.TryGetGate("nexusmods", out _).Should().BeTrue();

        await gate.ClearGate("nexusmods");
        gate.TryGetGate("nexusmods", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveGates_returns_live_gates_and_evicts_expired()
    {
        var gate = new InMemoryHostRateGate();
        await gate.GateHost("nexusmods", TimeSpan.FromMinutes(5), "live");
        await gate.GateHost("ko-fi", TimeSpan.FromMilliseconds(1), "expires");
        await Task.Delay(20);

        var active = await gate.GetActiveGates();
        active.Should().ContainSingle(g => g.HostTag == "nexusmods");
        active.Should().NotContain(g => g.HostTag == "ko-fi");
    }

    [Fact]
    public void GateHost_ignores_null_or_whitespace_host()
    {
        var gate = new InMemoryHostRateGate();
        Func<Task> a = () => gate.GateHost(null!, TimeSpan.FromMinutes(1), "test");
        Func<Task> b = () => gate.GateHost("   ", TimeSpan.FromMinutes(1), "test");
        // Should not throw; should not set anything.
        a.Should().NotThrowAsync();
        b.Should().NotThrowAsync();
    }

    [Fact]
    public void GateHost_ignores_zero_or_negative_duration()
    {
        var gate = new InMemoryHostRateGate();
        Func<Task> a = () => gate.GateHost("nexusmods", TimeSpan.Zero, "test");
        Func<Task> b = () => gate.GateHost("nexusmods", TimeSpan.FromSeconds(-5), "test");
        a.Should().NotThrowAsync();
        b.Should().NotThrowAsync();
        gate.TryGetGate("nexusmods", out _).Should().BeFalse();
    }
}
