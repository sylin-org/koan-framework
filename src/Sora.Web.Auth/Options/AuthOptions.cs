using System.ComponentModel.DataAnnotations;

namespace Sora.Web.Auth.Options;

public sealed class AuthOptions
{
    public const string SectionPath = "Sora:Web:Auth";

    // Dynamic provider defaults are convenient in Development but are gated in Production.
    // Set to true to allow dynamic providers (adapter defaults and contributors) to be enabled by default in Production.
    public bool AllowDynamicProvidersInProduction { get; init; }

    public ReturnUrlOptions ReturnUrl { get; init; } = new();
    public BffOptions Bff { get; init; } = new();
    public RateLimitOptions RateLimit { get; init; } = new();
    public TokensOptions Tokens { get; init; } = new();
    public ReConsentOptions ReConsent { get; init; } = new();

    // Providers keyed by id (e.g., google, microsoft, discord, corp-saml)
    public Dictionary<string, ProviderOptions> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReturnUrlOptions
{
    public string DefaultPath { get; init; } = "/";
    public string[] AllowList { get; init; } = Array.Empty<string>();
}

public sealed class BffOptions
{
    public bool Enabled { get; init; }
}

public sealed class RateLimitOptions
{
    public int ChallengesPerMinutePerIp { get; init; } = 10;
    public int CallbackFailuresPer10MinPerIp { get; init; } = 5;
}

public sealed class TokensOptions
{
    public bool PersistTokens { get; init; }
}

public sealed class ReConsentOptions
{
    public bool ForceOnLink { get; init; }
}

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
