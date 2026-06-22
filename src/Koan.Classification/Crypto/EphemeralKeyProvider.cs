using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Koan.Classification.Crypto;

/// <summary>
/// The dev/test tier of <see cref="IKeyProvider"/> (ARCH-0098 §3): per-tenant AES-256 keys generated on first use
/// and held in process memory only — nothing persisted, so a restart loses all keys (correct for dev: no durable
/// PII at rest). Implements the full §3a contract — per-tenant isolation, decrypt-by-owning-tenant independent of
/// the ambient tenant, count-aware rotation (a key rotates well before the AES-GCM random-nonce birthday bound),
/// and irreversible crypto-shred. Production uses the persisted, KMS-wrappable provider instead (Reference =
/// Intent replaces this).
/// </summary>
public sealed class EphemeralKeyProvider : IKeyProvider
{
    /// <summary>The sentinel bucket for a null/empty tenant — classification works without Koan.Tenancy.</summary>
    public const string HostBucket = "__host";

    /// <summary>Default rotation threshold (~1.07e9) — two orders of magnitude below the 2³² GCM nonce bound.</summary>
    public const long DefaultRotateAfter = 1L << 30;

    private readonly long _rotateAfter;

    private sealed class TenantKeys
    {
        public required FieldDataKey Active;
        public long Count;
        public readonly object Gate = new();
    }

    private sealed record KeyEntry(string Bucket, byte[] Material);

    private readonly ConcurrentDictionary<string, TenantKeys> _byTenant = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, KeyEntry> _byKeyId = new(StringComparer.Ordinal);

    public EphemeralKeyProvider(long rotateAfter = DefaultRotateAfter)
    {
        if (rotateAfter < 1) throw new ArgumentOutOfRangeException(nameof(rotateAfter), "Rotation threshold must be positive.");
        _rotateAfter = rotateAfter;
    }

    private static string Bucket(string? tenantId) => string.IsNullOrEmpty(tenantId) ? HostBucket : tenantId;

    public FieldDataKey GetActiveKey(string? tenantId)
    {
        var bucket = Bucket(tenantId);
        var tk = _byTenant.GetOrAdd(bucket, static (_, self) => new TenantKeys { Active = self.NewKey(_) }, this);
        lock (tk.Gate)
        {
            tk.Count++;
            if (tk.Count >= _rotateAfter)
            {
                // Count-aware rotation: a fresh active key; the retiring key stays in the keyId index so values
                // written under it still decrypt (only DestroyKey removes it).
                tk.Active = NewKey(bucket);
                tk.Count = 0;
            }
            return tk.Active;
        }
    }

    public FieldDataKey GetForDecrypt(string keyId)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyId);
        if (_byKeyId.TryGetValue(keyId, out var entry))
            return new FieldDataKey(keyId, entry.Material);
        throw new KeyUnavailableException(keyId);
    }

    public Task<IReadOnlyList<string>> DestroyKeyAsync(string? tenantId, CancellationToken ct = default)
    {
        var bucket = Bucket(tenantId);
        _byTenant.TryRemove(bucket, out _);

        var destroyed = new List<string>();
        foreach (var (keyId, entry) in _byKeyId.ToArray())
        {
            if (!string.Equals(entry.Bucket, bucket, StringComparison.Ordinal)) continue;
            if (_byKeyId.TryRemove(keyId, out var removed))
            {
                CryptographicOperations.ZeroMemory(removed.Material);   // crypto-shred: the material is gone
                destroyed.Add(keyId);
            }
        }
        return Task.FromResult<IReadOnlyList<string>>(destroyed);
    }

    private FieldDataKey NewKey(string bucket)
    {
        var material = RandomNumberGenerator.GetBytes(AesGcmFieldCipher.KeySize);
        var keyId = Guid.NewGuid().ToString("N");   // opaque; the owning tenant is the KeyEntry.Bucket mapping
        _byKeyId[keyId] = new KeyEntry(bucket, material);
        return new FieldDataKey(keyId, material);
    }
}
