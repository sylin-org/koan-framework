namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0006 D1 — the zero-config default key store: a single ES256 keypair generated once per process and
/// held in memory. Random by construction, never derivable from config, and gone at shutdown — exactly the
/// right posture for Development and single-process tests. A production Authorization Server replaces this
/// with a persisted, encrypted-at-rest store (so issued tokens survive a restart and the JWKS is stable);
/// the fail-closed boot guard refuses to start a production AS still on this ephemeral store.
/// </summary>
public sealed class EphemeralIssuerKeyStore : IIssuerKeyStore
{
    private readonly EcdsaKeyRing _ring = EcdsaKeyRing.Generate();

    public EcdsaKeyRing GetKeyRing() => _ring;
}
