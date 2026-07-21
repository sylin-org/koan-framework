using System;
using Koan.Data.Core.Model;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — a server-side, queryable, revocable grant of a capability to an agent, BEYOND whatever its token
/// carries. When the token alone is denied a gated action, the gate materializes the subject's active grants as
/// scoped effective-claims and re-evaluates (see <see cref="EntityFloorAuthorizationProvider"/>), so a grant
/// satisfies the coarse <c>Needs</c> without bypassing row <c>Constrain</c>. Grants are entities — the Koan move:
/// <c>Save()</c> to issue, <c>Remove()</c> to revoke (fleet-wide on the next call), <see cref="ExpiresAt"/> to
/// time-box, <c>Query(...)</c> to observe.
/// </summary>
public sealed class AgentGrant : Entity<AgentGrant>
{
    /// <summary>The agent's subject id — the principal's <c>sub</c> (bearer) or <c>NameIdentifier</c> (cookie).</summary>
    public string Subject { get; set; } = "";

    /// <summary>An <c>[Access]</c> term the grant confers — <c>"is:admin"</c> / <c>"has:scope:orders:fulfill"</c> /
    /// <c>"has:claim:tier=pro"</c>. <c>owner</c> is NOT grantable (ownership is row-bound <c>Constrain</c>, not a
    /// claim); <c>anyone</c>/<c>authenticated</c>/<c>origin</c> are not capabilities an agent can be lent.</summary>
    public string Capability { get; set; } = "";

    /// <summary>The entity name the grant applies to, or <c>"*"</c> for any.</summary>
    public string Resource { get; set; } = "*";

    /// <summary>Optional expiry; <c>null</c> = no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>True when the grant has not expired as of <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt.Value > now;

    /// <summary>True when this grant applies to <paramref name="resourceName"/> (exact match or the <c>"*"</c> wildcard).</summary>
    public bool AppliesTo(string resourceName)
        => Resource == "*" || string.Equals(Resource, resourceName, StringComparison.OrdinalIgnoreCase);
}
