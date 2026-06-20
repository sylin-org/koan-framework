using Microsoft.IdentityModel.Tokens;

namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 / SEC-0006 D1 — the asymmetric (ES256) issuer tier. Extends <see cref="IIssuer"/> with the one
/// capability a symmetric issuer structurally cannot have: a publishable <b>public</b> key set (JWKS), so a
/// verifier can validate tokens while holding only the public half — it can never mint. This is the issuer
/// the embedded Authorization Server (SEC-0006) signs with, the JWKS endpoint publishes, and the inbound
/// bearer scheme validates against.
/// <para>
/// Rotation is expressed through the key set: the active signing key plus any retiring keys whose tokens
/// have not yet expired all appear in <see cref="PublishedKeys"/>, so a rotated key keeps validating until
/// its last token dies (JWKS overlap).
/// </para>
/// </summary>
public interface IAsymmetricIssuer : IIssuer
{
    /// <summary>
    /// The public keys to publish at the JWKS endpoint — the active signing key first, then any retiring
    /// keys still inside their tokens' lifetime. Never contains a private component.
    /// </summary>
    IReadOnlyList<JsonWebKey> PublishedKeys { get; }
}
