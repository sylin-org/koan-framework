namespace Koan.Identity.Access;

/// <summary>
/// One contributing fact to an identity's effective access — a flattened, <b>revoke-addressable</b> grant. The
/// bidirectional explainer answers "why does X have access to Z?" with these rows, and "revoke" is
/// <c>Remove()</c> on the backing entity (<see cref="RowType"/> + <see cref="RowId"/>).
/// </summary>
/// <param name="Source">Where the fact came from — e.g. <c>IdentityRole</c>, <c>AgentGrant</c>, <c>Membership</c>.</param>
/// <param name="Kind"><c>role</c> or <c>capability</c>.</param>
/// <param name="Value">The role key or capability term.</param>
/// <param name="Resource">The entity/resource it applies to; <c>*</c> = any.</param>
/// <param name="Scope"><c>global</c> or a tenant id.</param>
/// <param name="RowType">The backing entity type name (for revoke).</param>
/// <param name="RowId">The backing entity id (for revoke).</param>
/// <param name="ExpiresAt">Optional expiry (for time-boxed grants).</param>
public sealed record AccessFact(
    string Source,
    string Kind,
    string Value,
    string Resource,
    string Scope,
    string RowType,
    string RowId,
    DateTimeOffset? ExpiresAt);

/// <summary>A revoke handle for a contributing <see cref="AccessFact"/>.</summary>
public sealed record AccessFactRef(string RowType, string RowId);
