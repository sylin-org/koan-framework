using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Providers;

/// <summary>
/// Shared builders for the well-known thin provider connectors (ARCH-0090). Post-WEB-0071 the
/// Discord/Google/Microsoft connectors are near-identical static default-contributors; their bodies
/// collapse to a single call here while each package keeps its own identity (Reference=Intent opt-in
/// preserved). A connector's <see cref="IAuthProviderContributor.GetDefaults"/> becomes a one-liner.
/// </summary>
public static class AuthProviderDefaults
{
    /// <summary>A single OIDC provider default keyed by <paramref name="id"/> (authority-based; the maintained OpenIdConnect handler discovers endpoints).</summary>
    public static IReadOnlyDictionary<string, ProviderOptions> Oidc(
        string id, string displayName, string icon, string authority, string[] scopes, int priority = 200)
        => new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [id] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.Oidc,
                DisplayName = displayName,
                Icon = icon,
                Authority = authority,
                Scopes = scopes,
                Enabled = true,
                Priority = priority
            }
        };

    /// <summary>A single OAuth2 provider default keyed by <paramref name="id"/> (explicit authorize/token/userinfo endpoints).</summary>
    public static IReadOnlyDictionary<string, ProviderOptions> OAuth2(
        string id, string displayName, string icon,
        string authorizationEndpoint, string tokenEndpoint, string userInfoEndpoint, string[] scopes, int priority = 150)
        => new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [id] = new ProviderOptions
            {
                Type = AuthConstants.Protocols.OAuth2,
                DisplayName = displayName,
                Icon = icon,
                AuthorizationEndpoint = authorizationEndpoint,
                TokenEndpoint = tokenEndpoint,
                UserInfoEndpoint = userInfoEndpoint,
                Scopes = scopes,
                Enabled = true,
                Priority = priority
            }
        };
}
