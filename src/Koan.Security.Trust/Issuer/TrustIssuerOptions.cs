using System.ComponentModel.DataAnnotations;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// Configuration for Trust's ES256 issuer. Signing material is supplied by <see cref="IIssuerKeyStore"/>;
/// secrets are not part of this public surface.
/// </summary>
public sealed class TrustIssuerOptions
{
    public const string SectionPath = "Koan:Security:Trust";

    /// <summary>
    /// The issuer (<c>iss</c>) the application names, or null when it names none. Unnamed, a token carries the issuer an
    /// owner above Trust states through <see cref="IIssuerIdentifier"/> — the embedded authorization server's, its base
    /// URL — else <c>koan-dev</c>. Named, it is kept: the authorization server states it too unless its own
    /// <c>Koan:Web:Auth:Server:Issuer</c> is set (SEC-0023). Whatever is stated, the bearer scheme also accepts this name.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>Token audience (<c>aud</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = "koan";

    /// <summary>Default credential lifetime in minutes.</summary>
    [Range(1, int.MaxValue)]
    public int DefaultLifetimeMinutes { get; init; } = 15;
}
