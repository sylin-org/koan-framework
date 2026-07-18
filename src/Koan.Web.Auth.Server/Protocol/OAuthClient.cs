using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D5 — a registered OAuth client. The row id is the <c>client_id</c>. Dynamic-registration clients
/// (RFC 7591) are <b>public</b> (no secret, PKCE-required) with loopback-only redirect URIs and a TTL; Entity-first
/// pre-registration can supply deliberate non-loopback redirects. <c>redirect_uri</c> is matched
/// <b>exact-string</b> (D4). Confidential clients/client secrets are not supported.
/// </summary>
public sealed class OAuthClient : Entity<OAuthClient>
{
    /// <summary>Untrusted display-only name (RFC 7591 <c>client_name</c>) — escape + label "unverified" on consent.</summary>
    public string ClientName { get; set; } = "";

    /// <summary>The exact redirect URIs this client may use. Exact-string match, no normalization.</summary>
    public List<string> RedirectUris { get; set; } = new();

    /// <summary>True when this client was created via open dynamic registration (constrains it to loopback redirects).</summary>
    public bool IsDynamic { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Optional expiry (dynamic clients are GC'd); <c>null</c> = no expiry.</summary>
    public DateTimeOffset? ExpiresUtc { get; set; }

    public bool IsActive(DateTimeOffset now) => ExpiresUtc is null || ExpiresUtc.Value > now;

    /// <summary>RFC 6749 / SEC-0006 D4 — exact-string redirect match (no scheme/host/path normalization).</summary>
    public bool AllowsRedirect(string redirectUri)
        => RedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.Ordinal));
}
