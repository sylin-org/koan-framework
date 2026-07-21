namespace Koan.Mcp.Execution;

/// <summary>
/// AN9 (docs/assessment/09 §11.3) — the "pin": a per-conversation correlation id for audit stitching and
/// continuity, carrying ZERO authority. It is client-OWNED — accepted opaque and UNTRUSTED (never
/// validated, deduped-for-trust, or associated with a grant) — and minted server-side (GUIDv7 via
/// <see cref="Koan.Core.StringId"/>) when the caller supplies none.
///
/// THE INVARIANT — continuity ≠ authority: the pin is NEVER consulted for permission. Authorization is
/// per-request against the principal (<see cref="McpToolAccessPolicy"/> takes only the principal, never a
/// correlation/session id). Accepting a pin in place of a grant would be session fixation.
/// </summary>
internal static class McpCorrelation
{
    /// <summary>The <see cref="Koan.Web.Endpoints.EntityRequestContext"/>.Items key the pin is threaded under.</summary>
    public const string ItemsKey = "mcp.correlationId";

    /// <summary>The argument name a client uses to supply its own correlation id.</summary>
    public const string ArgumentName = "correlationId";
}
