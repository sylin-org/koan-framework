using Microsoft.Extensions.Configuration;

namespace Koan.Security.Trust.Bootstrap;

/// <summary>SEC-0001 §4.1 — the trust posture (rung), detected from configuration.</summary>
public enum TrustMode
{
    /// <summary>In-process ephemeral ES256 issuer (Tier 0). Fine for dev; not for production.</summary>
    DevEphemeral,

    /// <summary>Shared-secret rung (<c>Koan:Security:Trust:Key</c>). Validation lands in Phase 4.</summary>
    SharedKey,

    /// <summary>A real issuer is configured (<c>Koan:Security:Trust:Issuer</c>) — fleet/federated.</summary>
    Configured,
}

/// <summary>SEC-0001 §4.1/§4.2 — trust-mode detection + the fail-closed production rule.</summary>
public static class TrustPosture
{
    public const string IssuerKey = "Koan:Security:Trust:Issuer";
    public const string SharedKeyKey = "Koan:Security:Trust:Key";
    public const string AllowEphemeralInProductionKey = "Koan:Security:Trust:AllowEphemeralIssuerInProduction";

    public static TrustMode Detect(IConfiguration cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg[IssuerKey])) return TrustMode.Configured;
        if (!string.IsNullOrWhiteSpace(cfg[SharedKeyKey])) return TrustMode.SharedKey;
        return TrustMode.DevEphemeral;
    }

    public static bool AllowEphemeralInProduction(IConfiguration cfg) =>
        string.Equals(cfg[AllowEphemeralInProductionKey], "true", StringComparison.OrdinalIgnoreCase);
}
