using System.Buffers.Binary;
using System.Text;

namespace Koan.Classification.Crypto;

/// <summary>
/// The self-describing ciphertext envelope for one encrypted field value (ARCH-0098 §3): the embedded
/// <see cref="KeyId"/> (so decrypt resolves the right key after rotation), the per-message <see cref="Nonce"/>,
/// the AES-GCM authentication <see cref="Tag"/>, and the <see cref="Ciphertext"/>. Serialized to a single opaque
/// string (a recognizable magic prefix + base64 of a length-prefixed binary pack) so it round-trips through any
/// adapter's value serialization and the read-reverse can cheaply tell ciphertext from a legacy plaintext value
/// (<see cref="TryParse"/>).
/// </summary>
/// <param name="KeyId">The opaque id of the key this value was encrypted under.</param>
/// <param name="Nonce">The per-message nonce (96-bit for GCM); never reused under one key.</param>
/// <param name="Ciphertext">The encrypted bytes (same length as the plaintext).</param>
/// <param name="Tag">The AES-GCM authentication tag (128-bit); decrypt fails closed if it does not verify.</param>
internal sealed record FieldCipherEnvelope(string KeyId, byte[] Nonce, byte[] Ciphertext, byte[] Tag)
{
    /// <summary>The version magic identifying a Koan field-encryption v1 envelope. Plaintext won't start with this.</summary>
    public const string Magic = "kfe1:";

    private const int MaxKeyIdBytes = 1024;       // bounds untrusted parse
    private const int MaxNonceBytes = 255;
    private const int MaxTagBytes = 255;

    /// <summary>Serialize to the opaque storage string: <c>kfe1:</c> + base64(packed binary).</summary>
    public string Serialize()
    {
        var keyIdBytes = Encoding.UTF8.GetBytes(KeyId);
        if (keyIdBytes.Length is 0 or > MaxKeyIdBytes)
            throw new InvalidOperationException("FieldCipherEnvelope KeyId is empty or too long to serialize.");

        // Layout: [keyIdLen:2 BE][keyId][nonceLen:1][nonce][tagLen:1][tag][ciphertext...]
        var total = 2 + keyIdBytes.Length + 1 + Nonce.Length + 1 + Tag.Length + Ciphertext.Length;
        var blob = new byte[total];
        var span = blob.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span[..2], (ushort)keyIdBytes.Length);
        var off = 2;
        keyIdBytes.CopyTo(span[off..]); off += keyIdBytes.Length;
        span[off++] = (byte)Nonce.Length;
        Nonce.CopyTo(span[off..]); off += Nonce.Length;
        span[off++] = (byte)Tag.Length;
        Tag.CopyTo(span[off..]); off += Tag.Length;
        Ciphertext.CopyTo(span[off..]);

        return Magic + Convert.ToBase64String(blob);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a serialized envelope, and if so the parsed form. Cheap prefix check
    /// first (a legacy plaintext value returns <c>false</c> without allocating). Bounds-checked: malformed input
    /// first (a legacy plaintext value returns <c>false</c> without allocating). Once the reserved prefix is
    /// present, malformed input fails loudly: silently treating a damaged protected value as plaintext would
    /// weaken the at-rest guarantee.
    /// </summary>
    public static bool TryParse(string? value, out FieldCipherEnvelope envelope)
    {
        envelope = null!;
        if (value is null || !value.StartsWith(Magic, StringComparison.Ordinal)) return false;
        try
        {
            var blob = Convert.FromBase64String(value[Magic.Length..]);
            var span = blob.AsSpan();
            if (span.Length < 2) throw Malformed();

            var keyIdLen = BinaryPrimitives.ReadUInt16BigEndian(span[..2]);
            var off = 2;
            if (keyIdLen is 0 or > MaxKeyIdBytes || off + keyIdLen > span.Length) throw Malformed();
            var keyId = Encoding.UTF8.GetString(span.Slice(off, keyIdLen)); off += keyIdLen;

            if (off >= span.Length) throw Malformed();
            int nonceLen = span[off++];
            if (nonceLen is 0 or > MaxNonceBytes || off + nonceLen > span.Length) throw Malformed();
            var nonce = span.Slice(off, nonceLen).ToArray(); off += nonceLen;

            if (off >= span.Length) throw Malformed();
            int tagLen = span[off++];
            if (tagLen is 0 or > MaxTagBytes || off + tagLen > span.Length) throw Malformed();
            var tag = span.Slice(off, tagLen).ToArray(); off += tagLen;

            var ciphertext = span[off..].ToArray();   // may be empty (encryption of an empty string)
            envelope = new FieldCipherEnvelope(keyId, nonce, ciphertext, tag);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new ClassificationIntegrityException("Classified field envelope is malformed.", exception);
        }

        static ClassificationIntegrityException Malformed()
            => new("Classified field envelope is malformed.");
    }

    /// <summary>Parse or throw — for callers that already know the value is an envelope.</summary>
    public static FieldCipherEnvelope Parse(string value)
        => TryParse(value, out var e)
            ? e
            : throw new ClassificationIntegrityException("Stored classified field is not a protected envelope.");
}
