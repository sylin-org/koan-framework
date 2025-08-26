namespace Sora.Web.Auth.Options;

public sealed class ProviderOptions
{
    // Common
    public string? Type { get; init; } // oidc | oauth2 | saml
    public string? DisplayName { get; init; }
    public string? Icon { get; init; }
    public bool Enabled { get; init; } = true;

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

    // SAML
    public string? EntityId { get; init; }
    public string? IdpMetadataUrl { get; init; }
    public string? IdpMetadataXml { get; init; }
    public string? SigningCertRef { get; init; }
    public string? DecryptionCertRef { get; init; }
    public bool AllowIdpInitiated { get; init; }
    public int ClockSkewSeconds { get; init; } = 120;
}