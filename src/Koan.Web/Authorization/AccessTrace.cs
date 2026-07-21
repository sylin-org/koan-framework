namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 forward-compat marker — recorded when an <c>owner</c> bag is allowed at the COARSE gate (degraded to
/// <c>authenticated</c>, no row bound). Computed but unused in Slice A; Slice C's per-row <c>can:[]</c> projection
/// consumes it so the manifest does not over-advertise a verb whose true permission is "pending row evaluation"
/// (a principal who owns no rows would otherwise see <c>can:[write]</c> for an owner-only write).
/// </summary>
public sealed record AccessTrace(bool OwnerDeferred);
