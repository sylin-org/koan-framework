using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D5 — a registered OAuth client. The row id is the <c>client_id</c>. Dynamic-registration clients
/// (RFC 7591) are <b>public</b> (no secret, PKCE-required) with loopback-only redirect URIs and a TTL; Entity-first
/// pre-registration can supply deliberate non-loopback redirects. <c>redirect_uri</c> is matched
/// <b>exact-string</b> (D4). Confidential clients/client secrets are not supported.
/// </summary>
public sealed class OAuthClient : Entity<OAuthClient>, IAmbientExempt
{
    /// <summary>Untrusted display-only name (RFC 7591 <c>client_name</c>) — escape + label "unverified" on consent.</summary>
    public string ClientName { get; set; } = "";

    /// <summary>The exact redirect URIs this client may use. Exact-string match, no normalization.</summary>
    public List<string> RedirectUris { get; set; } = new();

    /// <summary>
    /// The grants this client may use (RFC 7591 <c>grant_types</c>, values from <see cref="OAuthGrantTypes"/>). Empty
    /// means <see cref="OAuthGrantTypes.AuthorizationCode"/> only, RFC 7591's default. The Device Authorization Grant is
    /// allowed only to a client that names <see cref="OAuthGrantTypes.DeviceCode"/>; dynamic registration never does.
    /// </summary>
    public List<string> GrantTypes { get; set; } = new();

    /// <summary>True when this client was created via open dynamic registration (constrains it to loopback redirects).</summary>
    public bool IsDynamic { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Optional expiry (dynamic clients are GC'd); <c>null</c> = no expiry.</summary>
    public DateTimeOffset? ExpiresUtc { get; set; }

    public bool IsActive(DateTimeOffset now) => ExpiresUtc is null || ExpiresUtc.Value > now;

    /// <summary>True when this client may use <paramref name="grantType"/>: one it names, or the code grant when it names none.</summary>
    public bool AllowsGrant(string grantType)
        => GrantTypes.Count == 0
            ? string.Equals(grantType, OAuthGrantTypes.AuthorizationCode, StringComparison.Ordinal)
            : GrantTypes.Contains(grantType, StringComparer.Ordinal);

    /// <summary>
    /// True when the client <paramref name="clientId"/> exists, is active at <paramref name="now"/> and may use
    /// <paramref name="grantType"/>. Read at every step of a device request, so a client that loses the grant cannot
    /// finish a request it started while it had it (SEC-0024).
    /// </summary>
    internal static async Task<bool> Allows(string clientId, string grantType, DateTimeOffset now, CancellationToken ct)
        => await Get(clientId, ct) is { } client && client.IsActive(now) && client.AllowsGrant(grantType);

    /// <summary>RFC 6749 / SEC-0006 D4 — exact-string redirect match (no scheme/host/path normalization).</summary>
    public bool AllowsRedirect(string redirectUri)
        => RedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.Ordinal));
}
