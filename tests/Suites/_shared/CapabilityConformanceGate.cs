using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Xunit;

namespace Koan.Data.Conformance;

/// <summary>
/// What a conformance module does when the adapter does <b>not</b> announce its capability token — the
/// generalization of the Conformance Gate's "no-capability-lies" rule (ARCH-0094 §3) from the three AODB
/// isolation tokens to any <see cref="Capability"/>. A token that <b>is</b> announced always runs its
/// realization proof, so <i>over-claim</i> (declare-but-not-realize) fails green structurally regardless of
/// disposition; the disposition only decides the <i>under-claim</i> behavior.
/// </summary>
public enum UnclaimedDisposition
{
    /// <summary>The fleet mandate (ARCH-0103): the token MUST be announced. Under-claim fails the declares cell.
    /// The realization proof still runs — it is independent of the declaration.</summary>
    Required,

    /// <summary>Safety co-definition: under-claim is allowed, but the cell then proves the adapter <b>fails
    /// closed</b> on a scoped access rather than silently leaking (the vector <c>RowScoped</c> pattern for a
    /// pure-KNN store: declared ⇒ the overlay isolates; under-claimed ⇒ a scoped read throws).</summary>
    FailClosed,

    /// <summary>The ARCH-0094 §3 default: under-claim ⇒ the cell is <b>skipped, loud</b> — a visible xUnit skip,
    /// never a silent pass. For a non-safety token whose realization is simply out of scope when unannounced.</summary>
    Skip,
}

/// <summary>The action a realization cell takes for its token, decided from the adapter's announced capabilities.</summary>
public enum ConformanceCellAction
{
    /// <summary>Run the token's realization proof (the token is announced, or it is <see cref="UnclaimedDisposition.Required"/>).</summary>
    Realize,
    /// <summary>Prove the adapter fails closed on a scoped access (an under-claimed <see cref="UnclaimedDisposition.FailClosed"/> token).</summary>
    FailClosed,
    /// <summary>Skip the cell, loud (an under-claimed <see cref="UnclaimedDisposition.Skip"/> token).</summary>
    Skip,
}

/// <summary>
/// The capability-driven Conformance Gate dispatch (ARCH-0094 §3, Forge Phase 1) — the one place that turns an
/// adapter's announced <see cref="CapabilitySet"/> into per-token cell actions, seeded by the AODB isolation
/// modules and ready for any future token. The decision (<see cref="ResolveCell"/>) is pure; the action
/// (<see cref="RunCell"/>) is where the loud xUnit skip is raised, so a skip can never read as a silent green.
/// <para>
/// This type is link-compiled (not project-referenced) into each AODB testkit — see <c>tests/Suites/_shared/</c>,
/// the same pattern as <c>NonIsolatingFakeAdapter</c> — so the record testkit's discoverable conformance axis is
/// never dragged into the vector adapter hosts; the two planes share the gate's source, not an assembly. Invariant:
/// a single project must not reference BOTH testkits (it would see the link-compiled type twice); a future phase that
/// needs the gate across planes should promote it to a shared assembly instead.
/// </para>
/// </summary>
public static class CapabilityConformanceGate
{
    /// <summary>
    /// Decide what a realization cell should do for <paramref name="token"/> given the adapter's
    /// <paramref name="declared"/> capabilities and the token's <paramref name="whenUnclaimed"/> disposition.
    /// Announced ⇒ <see cref="ConformanceCellAction.Realize"/> (over-claim is then caught by the proof itself);
    /// unannounced ⇒ the disposition decides.
    /// </summary>
    public static ConformanceCellAction ResolveCell(CapabilitySet declared, Capability token, UnclaimedDisposition whenUnclaimed)
    {
        ArgumentNullException.ThrowIfNull(declared);
        if (declared.Has(token)) return ConformanceCellAction.Realize;
        return whenUnclaimed switch
        {
            // Run anyway — the realization is independent of the declaration; the declares cell catches the under-claim.
            UnclaimedDisposition.Required => ConformanceCellAction.Realize,
            UnclaimedDisposition.FailClosed => ConformanceCellAction.FailClosed,
            UnclaimedDisposition.Skip => ConformanceCellAction.Skip,
            _ => ConformanceCellAction.Skip,
        };
    }

    /// <summary>
    /// Run a realization cell through the gate. The token's disposition is looked up from the <paramref name="modules"/>
    /// table (the single source of truth — a cell never re-states it, so it cannot drift out of sync). Resolves the
    /// action for <paramref name="token"/>, then invokes <paramref name="realize"/> / <paramref name="failClosed"/>, or
    /// raises a loud xUnit skip. The skip path NEVER falls through to a silent pass — it calls
    /// <see cref="Assert.Skip(string)"/> and then throws, so a skip can only ever read as <i>skipped</i>, never green.
    /// </summary>
    public static Task RunCell(
        CapabilitySet declared,
        IReadOnlyList<(Capability Token, UnclaimedDisposition Disposition)> modules,
        Capability token,
        Func<Task> realize,
        Func<Task>? failClosed = null)
    {
        ArgumentNullException.ThrowIfNull(realize);
        var disposition = DispositionOf(modules, token);
        // Eager wiring check: a FailClosed module MUST supply its fail-closed proof — validated whether or not THIS
        // adapter announces the token, so a mis-wired cell is caught on every adapter, not only an under-claiming one.
        if (disposition == UnclaimedDisposition.FailClosed && failClosed is null)
            throw new InvalidOperationException(
                $"Token '{token}' has a FailClosed disposition but no fail-closed proof was supplied to RunCell.");

        switch (ResolveCell(declared, token, disposition))
        {
            case ConformanceCellAction.Realize:
                return realize();
            case ConformanceCellAction.FailClosed:
                return failClosed!();   // guaranteed non-null by the eager check above
            default: // Skip — loud: Assert.Skip throws to mark the cell skipped, so this is never a silent pass.
                Assert.Skip($"Capability '{token}' is not announced — its conformance module is skipped (capability-driven gate).");
                throw new UnreachableException("Assert.Skip must throw to mark the cell skipped.");
        }
    }

    /// <summary>Looks up <paramref name="token"/>'s disposition in the <paramref name="modules"/> table — the one place
    /// a token's disposition is declared. Throws when the token is not registered (a mis-wired cell), so a gated cell
    /// can never reference a token outside its ledger's module table.</summary>
    private static UnclaimedDisposition DispositionOf(
        IReadOnlyList<(Capability Token, UnclaimedDisposition Disposition)> modules, Capability token)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var (t, d) in modules)
            if (t == token) return d;
        throw new InvalidOperationException(
            $"Token '{token}' is not registered in the conformance Modules table — every gated cell's token must carry a disposition.");
    }

    /// <summary>
    /// Assert the adapter announced every token whose disposition is <see cref="UnclaimedDisposition.Required"/> —
    /// the fleet-mandate under-claim catcher for the declares cell. Optional tokens (FailClosed/Skip) are not required.
    /// </summary>
    public static void AssertRequiredDeclared(
        CapabilitySet declared,
        IReadOnlyList<(Capability Token, UnclaimedDisposition Disposition)> modules)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var (token, disposition) in modules)
            if (disposition == UnclaimedDisposition.Required)
                declared.Has(token).Should().BeTrue($"the adapter must announce the required capability '{token}'");
    }
}
