using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Conformance;
using Xunit;

namespace Koan.Data.Connector.InMemory.Tests;

/// <summary>
/// Unit proofs for the ARCH-0094 Phase 1 capability-driven Conformance Gate (the generalized "no-capability-lies"
/// dispatch). They pin the decision truth table across all three dispositions and prove the Skip path is <b>loud</b> —
/// it raises a visible xUnit skip and never runs its realization, so an unannounced module can never read as a silent
/// green. Hosted in this project only because it is the lightest Docker-free home that references the testkit carrying
/// the link-compiled gate; the gate itself is plane-agnostic.
/// </summary>
public sealed class CapabilityConformanceGateTests
{
    private static readonly Capability Tok = DataCaps.Isolation.RowScoped;
    private static readonly Capability OtherTok = DataCaps.Isolation.ContainerScoped;

    private static readonly (Capability, UnclaimedDisposition)[] RequiredMod = { (Tok, UnclaimedDisposition.Required) };
    private static readonly (Capability, UnclaimedDisposition)[] FailClosedMod = { (Tok, UnclaimedDisposition.FailClosed) };
    private static readonly (Capability, UnclaimedDisposition)[] SkipMod = { (Tok, UnclaimedDisposition.Skip) };

    private static CapabilitySet Declaring(params Capability[] tokens)
    {
        var set = new CapabilitySet("test");
        foreach (var t in tokens) set.Add(t);
        return set;
    }

    [Theory(DisplayName = "Gate: an announced token always Realizes (over-claim is then caught by the proof), whatever the disposition")]
    [InlineData(UnclaimedDisposition.Required)]
    [InlineData(UnclaimedDisposition.FailClosed)]
    [InlineData(UnclaimedDisposition.Skip)]
    public void Announced_token_realizes(UnclaimedDisposition disposition)
        => CapabilityConformanceGate.ResolveCell(Declaring(Tok), Tok, disposition)
            .Should().Be(ConformanceCellAction.Realize);

    [Fact(DisplayName = "Gate: an unannounced Required token still Realizes (the declares cell is the under-claim catcher)")]
    public void Unannounced_required_realizes()
        => CapabilityConformanceGate.ResolveCell(Declaring(), Tok, UnclaimedDisposition.Required)
            .Should().Be(ConformanceCellAction.Realize);

    [Fact(DisplayName = "Gate: an unannounced FailClosed token proves fail-closed instead of realizing")]
    public void Unannounced_failclosed_proves_failclosed()
        => CapabilityConformanceGate.ResolveCell(Declaring(), Tok, UnclaimedDisposition.FailClosed)
            .Should().Be(ConformanceCellAction.FailClosed);

    [Fact(DisplayName = "Gate: an unannounced Skip token skips")]
    public void Unannounced_skip_skips()
        => CapabilityConformanceGate.ResolveCell(Declaring(), Tok, UnclaimedDisposition.Skip)
            .Should().Be(ConformanceCellAction.Skip);

    [Fact(DisplayName = "Gate: RunCell runs the realization for an announced token")]
    public async Task RunCell_realizes_announced()
    {
        var ran = false;
        await CapabilityConformanceGate.RunCell(Declaring(Tok), RequiredMod, Tok,
            realize: () => { ran = true; return Task.CompletedTask; });
        ran.Should().BeTrue();
    }

    [Fact(DisplayName = "Gate: RunCell runs the fail-closed proof (not the realization) for an unannounced FailClosed token")]
    public async Task RunCell_runs_failclosed_proof()
    {
        bool realized = false, failClosed = false;
        await CapabilityConformanceGate.RunCell(Declaring(), FailClosedMod, Tok,
            realize: () => { realized = true; return Task.CompletedTask; },
            failClosed: () => { failClosed = true; return Task.CompletedTask; });
        failClosed.Should().BeTrue("the under-claimed FailClosed token must prove fail-closed");
        realized.Should().BeFalse("the realization must NOT run when the token is under-claimed");
    }

    [Fact(DisplayName = "Gate: a FailClosed module with no fail-closed proof throws EAGERLY — announced or not (a mis-wired cell is caught on every adapter)")]
    public async Task RunCell_failclosed_without_proof_throws_eagerly()
    {
        // Under-claimed: throws.
        Func<Task> underClaimed = () => CapabilityConformanceGate.RunCell(Declaring(), FailClosedMod, Tok,
            realize: () => Task.CompletedTask);
        await underClaimed.Should().ThrowAsync<InvalidOperationException>();

        // Announced: STILL throws — the eager check does not depend on announcement, so a mis-wired cell fails on
        // every adapter, not only the under-claiming one.
        Func<Task> announced = () => CapabilityConformanceGate.RunCell(Declaring(Tok), FailClosedMod, Tok,
            realize: () => Task.CompletedTask);
        await announced.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "Gate: RunCell throws when the token is not registered in the Modules table (a mis-wired cell)")]
    public async Task RunCell_unregistered_token_throws()
    {
        Func<Task> act = () => CapabilityConformanceGate.RunCell(Declaring(OtherTok), RequiredMod, OtherTok,
            realize: () => Task.CompletedTask);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "Gate: the Skip path is LOUD — it raises a visible xUnit skip and never runs the realization (never silently green)")]
    public async Task Skip_is_loud_never_silent_green()
    {
        var ran = false;
        Exception? raised = null;
        try
        {
            await CapabilityConformanceGate.RunCell(Declaring(), SkipMod, Tok,
                realize: () => { ran = true; return Task.CompletedTask; });
        }
        catch (Exception ex)
        {
            raised = ex;
        }

        ran.Should().BeFalse("a skipped module must NEVER execute its realization — that would read as a silent pass");
        raised.Should().NotBeNull("the Skip path must raise (a loud xUnit skip), never return normally");
        // Matched by name-substring, not a pinned type, on purpose: the gate stays decoupled from xUnit's internal
        // skip-exception type (it differs across xUnit versions); the real contract is just "this is a skip, not a pass".
        raised!.GetType().Name.Should().Contain("Skip",
            "the raised signal must be xUnit's skip exception, which marks the cell skipped (not passed)");
    }

    [Fact(DisplayName = "Gate: AssertRequiredDeclared passes when all Required tokens are announced, fails when one is missing")]
    public void AssertRequiredDeclared_enforces_required_only()
    {
        var modules = new (Capability, UnclaimedDisposition)[]
        {
            (DataCaps.Isolation.RowScoped, UnclaimedDisposition.Required),
            (DataCaps.Isolation.ContainerScoped, UnclaimedDisposition.Required),
            (DataCaps.Isolation.DatabaseScoped, UnclaimedDisposition.FailClosed), // optional — not required to be announced
        };

        // All Required announced (Database under-claimed is allowed — it is FailClosed): passes.
        var ok = Declaring(DataCaps.Isolation.RowScoped, DataCaps.Isolation.ContainerScoped);
        Action passes = () => CapabilityConformanceGate.AssertRequiredDeclared(ok, modules);
        passes.Should().NotThrow();

        // A Required token (ContainerScoped) under-claimed: fails loud.
        var underClaimed = Declaring(DataCaps.Isolation.RowScoped);
        Action fails = () => CapabilityConformanceGate.AssertRequiredDeclared(underClaimed, modules);
        fails.Should().Throw<Exception>("under-claim of a Required token must fail the declares assertion");
    }
}
