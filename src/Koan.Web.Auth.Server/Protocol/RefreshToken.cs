using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D9 — a rotating refresh token. The row id is the <b>hash</b> of the opaque token (the raw value is
/// never stored). Tokens rotate on every use: redeeming one mints a fresh access + refresh pair in the same
/// <see cref="FamilyId"/> and marks this row <see cref="Rotated"/>. Replaying a rotated token is reuse — the
/// whole family is revoked (OAuth 2.1 §4.3.1). The token is backed by a revocable grant
/// (<see cref="GrantId"/>): revoke the grant and the next refresh fails closed.
/// </summary>
public sealed class RefreshToken : Entity<RefreshToken>
{
    /// <summary>The rotation family — every descendant of one authorization shares it (reuse-detection scope).</summary>
    [Index]
    public string FamilyId { get; set; } = "";

    public string ClientId { get; set; } = "";
    public string Resource { get; set; } = "";

    // The subject identity to mint future access tokens from (the refresh endpoint has no session).
    public string Subject { get; set; } = "";
    public string? SubjectName { get; set; }
    public string? SubjectEmail { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> GrantedScopes { get; set; } = new();

    /// <summary>The backing revocable grant (SEC-0005 AgentGrant id). Gone ⇒ the refresh fails closed.</summary>
    public string GrantId { get; set; } = "";

    /// <summary>True once redeemed — a second redemption is reuse and revokes the family.</summary>
    public bool Rotated { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;
}
