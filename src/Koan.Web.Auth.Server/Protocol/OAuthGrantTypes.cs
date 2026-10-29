namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// The OAuth grant types the embedded authorization server implements, as RFC 7591 <c>grant_types</c> and the
/// token endpoint's <c>grant_type</c> name them. A client is allowed a grant through <see cref="OAuthClient.GrantTypes"/>.
/// </summary>
public static class OAuthGrantTypes
{
    /// <summary>Authorization Code with PKCE (RFC 6749 §4.1, RFC 7636) — RFC 7591's default for a client that names none.</summary>
    public const string AuthorizationCode = "authorization_code";

    /// <summary>The Device Authorization Grant (RFC 8628). A client is allowed it only by naming it.</summary>
    public const string DeviceCode = "urn:ietf:params:oauth:grant-type:device_code";

    /// <summary>Refresh-token rotation (RFC 6749 §6), issued beside a code or device grant when refresh tokens are on.</summary>
    public const string RefreshToken = "refresh_token";
}
