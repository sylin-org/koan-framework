using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// The ES256 (P-256) signing material an asymmetric issuer holds: one active key plus any retiring keys kept
/// alive for JWKS overlap during rotation. This is the single, shared key-material primitive — the embedded
/// Authorization Server and the dev OIDC test provider both sign through it, so there is exactly one place
/// that generates P-256 keypairs and projects their <b>public</b> halves to JWKs.
/// </summary>
public sealed class EcdsaKeyRing
{
    /// <summary>SEC-0001 §6.1 — one fixed asymmetric suite; the verifier pins it (no in-band negotiation).</summary>
    public const string Algorithm = SecurityAlgorithms.EcdsaSha256; // "ES256"

    public EcdsaKeyRing(ECDsaSecurityKey active, IReadOnlyList<ECDsaSecurityKey>? retiring = null)
    {
        Active = active ?? throw new ArgumentNullException(nameof(active));
        Retiring = retiring ?? [];
    }

    /// <summary>The current signing key (its <c>kid</c> is the <c>kid</c> header of freshly-minted tokens).</summary>
    public ECDsaSecurityKey Active { get; }

    /// <summary>Keys rotated out but still inside their tokens' lifetime — validate-only, never signed with.</summary>
    public IReadOnlyList<ECDsaSecurityKey> Retiring { get; }

    /// <summary>Active + retiring — every key a verifier must accept right now.</summary>
    public IReadOnlyList<ECDsaSecurityKey> All => [Active, .. Retiring];

    /// <summary>Generate a fresh, random P-256 keypair as a single-key ring (no retiring keys).</summary>
    public static EcdsaKeyRing Generate()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ecdsa) { KeyId = Guid.NewGuid().ToString("n") };
        return new EcdsaKeyRing(key);
    }

    /// <summary>
    /// The <b>public</b> JWKs for every key in the ring (active first), <c>use=sig</c>, <c>alg=ES256</c>.
    /// Built from a public-only parameter export so a private component can never leak into the JWKS —
    /// this does not rely on the converter's include-private default.
    /// </summary>
    public IReadOnlyList<JsonWebKey> ToPublicJwks() => [.. All.Select(ToPublicJwk)];

    /// <summary>The public JWK for a single ES256 key — explicitly public-only.</summary>
    public static JsonWebKey ToPublicJwk(ECDsaSecurityKey key)
    {
        var p = key.ECDsa.ExportParameters(includePrivateParameters: false);
        if (p.Q.X is null || p.Q.Y is null)
            throw new InvalidOperationException($"ES256 key '{key.KeyId}' has no public coordinates to publish.");
        return new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Base64UrlEncoder.Encode(p.Q.X),
            Y = Base64UrlEncoder.Encode(p.Q.Y),
            Use = "sig",
            Alg = Algorithm,
            Kid = key.KeyId,
        };
    }
}
