using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D4 — an authorization code. The row id is the opaque, single-use code. It is bound at issue-time
/// (on consent approval) to <c>(client_id, redirect_uri, code_challenge, scope, subject, resource)</c>, and every
/// field is re-verified at <c>/oauth/token</c>; PKCE-S256 is mandatory. Single-use: the row is marked
/// <see cref="Consumed"/> on redemption and kept until expiry so a replay is detected (and the issued token
/// family revoked — D9). It also carries the consented subject identity, since <c>/oauth/token</c> has no
/// browser session to read.
/// </summary>
public sealed class AuthorizationCode : Entity<AuthorizationCode>
{
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string Resource { get; set; } = "";

    // The consented subject identity, captured at approval (no session at the token endpoint).
    public string Subject { get; set; } = "";
    public string? SubjectName { get; set; }
    public string? SubjectEmail { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> GrantedScopes { get; set; } = new();

    public bool Consumed { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresUtc <= now;
}
