using System.ComponentModel.DataAnnotations;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// Configuration for Trust's ES256 issuer. Signing material is supplied by <see cref="IIssuerKeyStore"/>;
/// secrets are not part of this public surface.
/// </summary>
public sealed class TrustIssuerOptions
{
    public const string SectionPath = "Koan:Security:Trust";

    /// <summary>Token issuer (<c>iss</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; init; } = "koan-dev";

    /// <summary>Token audience (<c>aud</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; init; } = "koan";

    /// <summary>Default credential lifetime in minutes.</summary>
    [Range(1, int.MaxValue)]
    public int DefaultLifetimeMinutes { get; init; } = 15;
}
