namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0006 D1 — the source of an asymmetric issuer's ES256 key material. The seam that lets the key
/// lifecycle vary by deployment tier without the issuer caring: the dev / single-process default is an
/// ephemeral per-process keypair (<see cref="EphemeralIssuerKeyStore"/>); a production deployment supplies
/// a key store that persists the key encrypted-at-rest (via the data layer + <c>IDataProtector</c>) and
/// rotates it with JWKS overlap. Reference = Intent: referencing the persisted store activates it.
/// </summary>
public interface IIssuerKeyStore
{
    /// <summary>
    /// The current key ring (active signing key + any retiring keys still inside their tokens' lifetime).
    /// Implementations cache; this is called on the mint/validate hot path.
    /// </summary>
    EcdsaKeyRing GetKeyRing();
}
