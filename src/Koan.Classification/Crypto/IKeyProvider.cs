namespace Koan.Classification.Crypto;

/// <summary>
/// The source of per-tenant field-encryption keys (ARCH-0098 §3 / §3a). Env-tiered behind Reference = Intent: an
/// ephemeral in-memory provider for dev/test (<see cref="EphemeralKeyProvider"/>) and a persisted, KMS-wrappable
/// provider for production. The asymmetry between the three methods is load-bearing:
///
/// <list type="bullet">
///   <item><b>Encrypt</b> uses the ambient tenant's <i>active</i> key (<see cref="GetActiveKey"/>) — count-aware
///   so a key is rotated well before the AES-GCM random-nonce birthday bound (~2³² messages).</item>
///   <item><b>Decrypt</b> resolves the key by the envelope's <c>KeyId</c> (<see cref="GetForDecrypt"/>),
///   <b>independent of the ambient tenant</b> — so background jobs, admin tooling, and migration sagas that run
///   under a different (or no) ambient tenant still decrypt the owning tenant's data. A retired key keeps
///   decrypting; only <see cref="DestroyKeyAsync"/> removes it.</item>
///   <item><b>Crypto-shred</b> (<see cref="DestroyKeyAsync"/>) destroys a tenant's keys, rendering all of that
///   tenant's at-rest ciphertext permanently unrecoverable — the mechanism behind the erasure certificate.
///   Irreversible and idempotent.</item>
/// </list>
///
/// <para>A <c>null</c>/empty tenant id denotes the <b>host</b> key bucket, so classification works in a
/// single-tenant (no <c>Koan.Tenancy</c>) app — the axes are independent.</para>
/// </summary>
public interface IKeyProvider
{
    /// <summary>The active key for <paramref name="tenantId"/> (host bucket when null/empty). Counts toward rotation.</summary>
    FieldDataKey GetActiveKey(string? tenantId);

    /// <summary>
    /// The key identified by <paramref name="keyId"/> — its owning tenant's key, regardless of the ambient tenant.
    /// Throws <see cref="KeyUnavailableException"/> if the key is unknown or has been crypto-shredded.
    /// </summary>
    FieldDataKey GetForDecrypt(string keyId);

    /// <summary>
    /// Crypto-shred every key of <paramref name="tenantId"/> (host bucket when null/empty). Irreversible and
    /// idempotent: after this, <see cref="GetForDecrypt"/> for those keys throws and the tenant's data is
    /// unrecoverable. Returns the destroyed key ids (for the erasure certificate).
    /// </summary>
    Task<IReadOnlyList<string>> DestroyKeyAsync(string? tenantId, CancellationToken ct = default);
}
