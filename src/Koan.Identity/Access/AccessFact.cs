namespace Koan.Identity.Access;

/// <summary>
/// One contributing fact to an identity's effective access — a flattened, <b>revoke-addressable</b> grant. The
/// bidirectional explainer answers "why does X have access to Z?" with these rows, and "revoke" is
/// <c>Remove()</c> on the backing entity (<see cref="RowType"/> + <see cref="RowId"/>).
/// </summary>
/// <param name="Source">Where the fact came from — e.g. <c>Role</c>, <c>PermissionOverwrite</c>, <c>AgentGrant</c>, <c>Membership</c>.</param>
/// <param name="Kind">One of <see cref="AccessFactKind"/>: a role, a capability, a permission, a deny, or an ungranted permission.</param>
/// <param name="Value">The role key, capability term or permission token.</param>
/// <param name="Resource">The entity/resource it applies to; <c>*</c> = any.</param>
/// <param name="Scope"><see cref="AccessFactScope.Global"/> or a tenant id.</param>
/// <param name="RowType">The backing entity type name (for revoke); empty when no row backs the fact.</param>
/// <param name="RowId">The backing entity id (for revoke); empty when no row backs the fact.</param>
/// <param name="ExpiresAt">Optional expiry (for time-boxed grants and overwrites).</param>
/// <param name="Subject">The subject the backing row names: the member a role membership revoke removes, or the role
/// key or person id an overwrite is for.</param>
public sealed record AccessFact(
    string Source,
    string Kind,
    string Value,
    string Resource,
    string Scope,
    string RowType,
    string RowId,
    DateTimeOffset? ExpiresAt,
    string? Subject = null)
{
    /// <summary>The revoke handle for exactly this fact.</summary>
    public AccessFactRef Ref => new(RowType, RowId, Subject, Kind, Value);
}

/// <summary>
/// A revoke handle for a contributing <see cref="AccessFact"/>. <see cref="Kind"/> and <see cref="Value"/> name the one
/// fact when a row backs several — an overwrite's allowed or withheld permission — so a revoke removes that fact and
/// nothing else; such a row refuses a handle that omits them.
/// </summary>
public sealed record AccessFactRef(string RowType, string RowId, string? Subject = null, string? Kind = null, string? Value = null);
