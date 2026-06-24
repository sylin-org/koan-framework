namespace Koan.Data.Core;

/// <summary>
/// ARCH-0100: a module-registered seam that lets one cross-cutting ambient axis (tenant, classification, …)
/// <b>survive a durable async-hop</b>. The <see cref="EntityContext"/> typed-slice carrier (ARCH-0097) lives in
/// an <c>AsyncLocal</c>, which is lost when work serializes onto a job ledger / message and resumes on another
/// thread, scope, process, or node. A carrier snapshots its slice to a portable string at submit
/// (<see cref="Capture"/>) and pushes it back onto the ambient context at execute (<see cref="Restore"/>).
///
/// <para>The data core stays axis-agnostic: it never names "tenant". A concern with no registered carrier is
/// simply absent (Reference = Intent). Carriers are discovered DI-enumerable (one per module's <c>Register</c>),
/// so the set is fixed at host build — before any submit can capture.</para>
///
/// <para>This is a small behavioural seam (the shape of <c>IStorageGuard</c>/<c>IWriteStamp</c>), not a
/// declarative descriptor: <see cref="Restore"/> genuinely pushes an ambient scope.</para>
/// </summary>
public interface IAmbientSliceCarrier
{
    /// <summary>Stable, opaque key for this axis (e.g. <c>"koan:tenant"</c>) — the carrier bag key. Must be unique.</summary>
    string AxisKey { get; }

    /// <summary>
    /// Read the current ambient slice and serialize it to a portable string, or <c>null</c> when no such slice is
    /// in scope. The captured string <b>must</b> lead with a carrier-owned version token so a future format change
    /// fails closed at <see cref="Restore"/> rather than mis-restoring (ARCH-0100 §3/§6).
    /// </summary>
    string? Capture();

    /// <summary>
    /// Push the slice described by <paramref name="captured"/> back onto <see cref="EntityContext"/> for the
    /// lifetime of the returned scope; disposing restores the previous context. Throw (with a clear reason) to
    /// refuse — the job dead-letters rather than running fail-open in a wrong/ghost ambient.
    /// </summary>
    System.IDisposable Restore(string captured);

    /// <summary>
    /// Push an <b>explicitly cleared</b> ambient for this axis (no slice) for the lifetime of the returned scope;
    /// disposing restores the previous context. Used at execute when the captured bag carries no value for this
    /// axis, so the work never <i>inherits</i> the carrier thread's ambient (e.g. an inline drain running inside a
    /// caller's <c>Tenant.Use</c> scope) — an unscoped job observes a genuinely absent axis, not a leaked one.
    /// </summary>
    System.IDisposable Suppress();
}
