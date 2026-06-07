namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 — configuration for the trust issuer. Sane dev defaults; the fleet/federation tiers (Phases
/// 5/7) extend this with issuer URI / signing-key backing without changing the consumption surface.
/// </summary>
public sealed class TrustIssuerOptions
{
    public const string SectionPath = "Koan:Security:Trust";

    /// <summary>Token issuer (<c>iss</c>).</summary>
    public string Issuer { get; init; } = "koan-dev";

    /// <summary>Token audience (<c>aud</c>).</summary>
    public string Audience { get; init; } = "koan";

    /// <summary>Default credential lifetime (SEC-0001 §6.3: ~15 min at the edge).</summary>
    public int DefaultLifetimeMinutes { get; init; } = 15;
}
