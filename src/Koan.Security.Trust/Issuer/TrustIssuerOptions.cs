namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 / SEC-0003 — configuration for the trust issuer. The default <see cref="Key"/> is the well-known
/// insecure shared secret so every service self-mints zero-config; override it for any non-dev deployment
/// (and you must — the boot guard fails closed outside Development, SEC-0003 §2.5).
/// </summary>
public sealed class TrustIssuerOptions
{
    public const string SectionPath = "Koan:Security:Trust";

    /// <summary>
    /// SEC-0003 §2.4 — the well-known default shared secret. <b>Honest-insecure by name</b>: it is public and
    /// forgeable, exists so zero-config self-mint "just works" in dev, and is structurally barred from
    /// non-dev environments (fail-closed) with a very loud boot warning while active.
    /// </summary>
    public const string DefaultInsecureKey = "super-insecure-shared-secret-replace-asap";

    /// <summary>Token issuer (<c>iss</c>).</summary>
    public string Issuer { get; init; } = "koan-dev";

    /// <summary>Token audience (<c>aud</c>).</summary>
    public string Audience { get; init; } = "koan";

    /// <summary>
    /// SEC-0003 §2.4 — the HS256 shared secret. Defaults to <see cref="DefaultInsecureKey"/> so every service
    /// self-mints with zero config and services sharing this value trust each other's tokens. The actual HMAC
    /// key is <c>SHA-256(Key)</c>, so any length is valid and all holders derive the same key.
    /// </summary>
    public string Key { get; init; } = DefaultInsecureKey;

    /// <summary>Default credential lifetime (SEC-0001 §6.3: ~15 min at the edge).</summary>
    public int DefaultLifetimeMinutes { get; init; } = 15;
}
