using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Koan.Classification.Crypto;

/// <summary>
/// Development-only key provider. Keys are isolated by compiled segmentation scope, rotate by encryption count, and
/// remain in memory only; disposing the host zeroes all retained material.
/// </summary>
public sealed class EphemeralClassificationKeyProvider : IClassificationKeyProvider, IDisposable
{
    public const long DefaultRotateAfter = 1L << 30;

    private readonly long _rotateAfter;
    private readonly ConcurrentDictionary<string, ScopeKeys> _byScope = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, KeyEntry> _byKeyId = new(StringComparer.Ordinal);
    private int _disposed;

    private sealed class ScopeKeys
    {
        public required ClassificationDataKey Active;
        public long Count;
        public readonly object Gate = new();
    }

    private sealed record KeyEntry(byte[] Material);

    public EphemeralClassificationKeyProvider(long rotateAfter = DefaultRotateAfter)
    {
        if (rotateAfter < 1)
            throw new ArgumentOutOfRangeException(nameof(rotateAfter), "Rotation threshold must be positive.");
        _rotateAfter = rotateAfter;
    }

    public ClassificationDataKey GetActiveKey(string scope)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var scoped = _byScope.GetOrAdd(
            scope,
            static (_, self) => new ScopeKeys { Active = self.NewKey() },
            this);
        lock (scoped.Gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (scoped.Count >= _rotateAfter)
            {
                scoped.Active = NewKey();
                scoped.Count = 0;
            }

            scoped.Count++;
            return scoped.Active;
        }
    }

    public ClassificationDataKey GetForDecrypt(string keyId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (_byKeyId.TryGetValue(keyId, out var entry))
            return new ClassificationDataKey(keyId, entry.Material);
        throw new ClassificationKeyUnavailableException(keyId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var entry in _byKeyId.Values)
            CryptographicOperations.ZeroMemory(entry.Material);
        _byKeyId.Clear();
        _byScope.Clear();
    }

    private ClassificationDataKey NewKey()
    {
        var material = RandomNumberGenerator.GetBytes(AesGcmFieldCipher.KeySize);
        var keyId = Guid.NewGuid().ToString("N");
        if (!_byKeyId.TryAdd(keyId, new KeyEntry(material)))
        {
            CryptographicOperations.ZeroMemory(material);
            return NewKey();
        }

        return new ClassificationDataKey(keyId, material);
    }
}
