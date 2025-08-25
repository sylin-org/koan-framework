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