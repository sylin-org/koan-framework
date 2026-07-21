using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Koan.Core.Context;

/// <summary>
/// Computes a deterministic, value-opaque SHA-256 identity for a captured Koan context and optional logical identity
/// parts. It is suitable for dedupe and durable record keys; it is not encryption or proof of provenance.
/// </summary>
public static class KoanContextFingerprint
{
    private const string Domain = "koan-context-fingerprint-v1";

    /// <summary>
    /// Hashes an ordinally canonical, length-delimited representation of <paramref name="captured"/> and
    /// <paramref name="identityParts"/>. Enumeration order is canonical, and delimiter characters cannot create
    /// ambiguous value boundaries.
    /// </summary>
    public static string Compute(
        IReadOnlyDictionary<string, string>? captured,
        params string[] identityParts)
    {
        ArgumentNullException.ThrowIfNull(identityParts);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, Domain);
        AppendInt32(hash, identityParts.Length);
        foreach (var part in identityParts)
        {
            if (part is null)
                throw new ArgumentException("A context fingerprint identity part cannot be null.", nameof(identityParts));
            AppendString(hash, part);
        }

        AppendInt32(hash, captured?.Count ?? 0);
        if (captured is not null)
        {
            foreach (var pair in captured.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (pair.Key is null || pair.Value is null)
                    throw new ArgumentException("A context fingerprint bag cannot contain null keys or values.", nameof(captured));
                AppendString(hash, pair.Key);
                AppendString(hash, pair.Value);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
