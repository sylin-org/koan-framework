using Microsoft.Extensions.Configuration;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust.Bootstrap;

/// <summary>SEC-0001 §4.1 / SEC-0003 §4 — the trust posture (rung), detected from configuration.</summary>
public enum TrustMode
{
    /// <summary>
    /// The key is unset or equals the well-known default insecure secret (Tier 0). Fine for local development;
    /// fail-closed outside Development (SEC-0003 §2.5).
    /// </summary>
    DefaultInsecure,

    /// <summary>A custom shared secret is set (<c>Koan:Security:Trust:Key</c>). Solo / small trusted team / staging.</summary>
    SharedKey,

    /// <summary>A real issuer is configured (<c>Koan:Security:Trust:Issuer</c>) — fleet / federated (future, Rung 2).</summary>
    Configured,
}

/// <summary>SEC-0001 §4.1/§4.2 / SEC-0003 — trust-mode detection + the fail-closed production rule.</summary>
public static class TrustPosture
{
    public const string IssuerKey = "Koan:Security:Trust:Issuer";
    public const string SharedKeyKey = "Koan:Security:Trust:Key";
    public const string AllowInsecureKeyInProductionKey = "Koan:Security:Trust:AllowInsecureKeyInProduction";

    public static TrustMode Detect(IConfiguration cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg[IssuerKey])) return TrustMode.Configured;

        var key = cfg[SharedKeyKey];
        // A non-empty key that is NOT the well-known default ⇒ a real shared secret.
        if (!string.IsNullOrWhiteSpace(key) && !string.Equals(key, TrustIssuerOptions.DefaultInsecureKey, StringComparison.Ordinal))
            return TrustMode.SharedKey;

        // Unset (⇒ options default to the insecure key) or explicitly the well-known default.
        return TrustMode.DefaultInsecure;
    }

    public static bool AllowInsecureKeyInProduction(IConfiguration cfg) =>
        string.Equals(cfg[AllowInsecureKeyInProductionKey], "true", StringComparison.OrdinalIgnoreCase);
}
