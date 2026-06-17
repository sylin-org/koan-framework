namespace Koan.Web.Auth.Options;

public sealed class ProviderOptions
{
    // Common
    public string? Type { get; init; } // oidc | oauth2
    public string? DisplayName { get; init; }
    public string? Icon { get; init; }
    public bool Enabled { get; init; } = true;
    public int? Priority { get; init; }

    // OIDC
    public string? Authority { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? SecretRef { get; init; }
    public string[]? Scopes { get; init; }
    public string? CallbackPath { get; init; }

    // OAuth2 generic
    public string? AuthorizationEndpoint { get; init; }
    public string? TokenEndpoint { get; init; }
    public string? UserInfoEndpoint { get; init; }

    /// <summary>
    /// The single canonical merge: <paramref name="overlay"/> (e.g. user config) over
    /// <paramref name="baseline"/> (e.g. a contributor default), field-wins-if-set. Replaces the three
    /// hand-copied merge bodies that previously lived in ProviderRegistry + KoanAutoRegistrar (E5).
    /// </summary>
    public static ProviderOptions Merge(ProviderOptions baseline, ProviderOptions overlay) => new()
    {
        Type = overlay.Type ?? baseline.Type,
        DisplayName = overlay.DisplayName ?? baseline.DisplayName,
        Icon = overlay.Icon ?? baseline.Icon,
        Enabled = overlay.Enabled && baseline.Enabled,
        Priority = overlay.Priority ?? baseline.Priority,
        Authority = overlay.Authority ?? baseline.Authority,
        ClientId = overlay.ClientId ?? baseline.ClientId,
        ClientSecret = overlay.ClientSecret ?? baseline.ClientSecret,
        SecretRef = overlay.SecretRef ?? baseline.SecretRef,
        Scopes = overlay.Scopes ?? baseline.Scopes,
        CallbackPath = overlay.CallbackPath ?? baseline.CallbackPath,
        AuthorizationEndpoint = overlay.AuthorizationEndpoint ?? baseline.AuthorizationEndpoint,
        TokenEndpoint = overlay.TokenEndpoint ?? baseline.TokenEndpoint,
        UserInfoEndpoint = overlay.UserInfoEndpoint ?? baseline.UserInfoEndpoint,
    };

    /// <summary>Clone with <see cref="Enabled"/> overridden (e.g. production-gating a non-explicit provider off).</summary>
    public static ProviderOptions WithEnabled(ProviderOptions p, bool enabled) => new()
    {
        Type = p.Type,
        DisplayName = p.DisplayName,
        Icon = p.Icon,
        Enabled = enabled,
        Priority = p.Priority,
        Authority = p.Authority,
        ClientId = p.ClientId,
        ClientSecret = p.ClientSecret,
        SecretRef = p.SecretRef,
        Scopes = p.Scopes,
        CallbackPath = p.CallbackPath,
        AuthorizationEndpoint = p.AuthorizationEndpoint,
        TokenEndpoint = p.TokenEndpoint,
        UserInfoEndpoint = p.UserInfoEndpoint,
    };
}
