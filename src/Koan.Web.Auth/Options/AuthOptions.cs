using System.ComponentModel.DataAnnotations;

namespace Koan.Web.Auth.Options;

public sealed class AuthOptions
{
    public const string SectionPath = "Koan:Web:Auth";

    public ReturnUrlOptions ReturnUrl { get; init; } = new();
    public BffOptions Bff { get; init; } = new();
    public RateLimitOptions RateLimit { get; init; } = new();
    public TokensOptions Tokens { get; init; } = new();
    public ReConsentOptions ReConsent { get; init; } = new();
    public string? PreferredProviderId { get; init; }

    // Providers keyed by id (e.g., google, microsoft, discord, corp-saml)
    public Dictionary<string, ProviderOptions> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
