using System.ComponentModel;
using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Providers;

/// <summary>
/// Immutable availability declaration registered by one authentication connector. Functional Web Auth combines it
/// with application configuration and owns eligibility, election, scheme creation, and reporting.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record AuthProviderDefinition(
    string Id,
    ProviderOptions Defaults,
    bool Automatic = false,
    bool Available = true,
    string? AvailabilityReason = null)
{
    public static AuthProviderDefinition Oidc(
        string id,
        string displayName,
        string icon,
        string authority,
        string[] scopes,
        int priority = 200,
        bool automatic = false,
        bool available = true,
        string? availabilityReason = null)
        => new(
            id,
            new ProviderOptions
            {
                Type = AuthProviderProtocols.Oidc,
                DisplayName = displayName,
                Icon = icon,
                Authority = authority,
                Scopes = scopes,
                Priority = priority
            },
            automatic,
            available,
            availabilityReason);

    public static AuthProviderDefinition OAuth2(
        string id,
        string displayName,
        string icon,
        string authorizationEndpoint,
        string tokenEndpoint,
        string userInfoEndpoint,
        string[] scopes,
        int priority = 150,
        bool automatic = false,
        bool available = true,
        string? availabilityReason = null)
        => new(
            id,
            new ProviderOptions
            {
                Type = AuthProviderProtocols.OAuth2,
                DisplayName = displayName,
                Icon = icon,
                AuthorizationEndpoint = authorizationEndpoint,
                TokenEndpoint = tokenEndpoint,
                UserInfoEndpoint = userInfoEndpoint,
                Scopes = scopes,
                Priority = priority
            },
            automatic,
            available,
            availabilityReason);
}

/// <summary>Stable protocol identifiers accepted by Web Auth configuration and connector definitions.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AuthProviderProtocols
{
    public const string Oidc = "oidc";
    public const string OAuth2 = "oauth2";
}
