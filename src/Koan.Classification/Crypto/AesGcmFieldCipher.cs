using System.Security.Cryptography;

namespace Koan.Classification.Crypto;

/// <summary>
/// AES-256-GCM field cipher (ARCH-0098 §3) — authenticated encryption with a fresh random 96-bit nonce per
/// message and a 128-bit tag. Stateless and thread-safe (each call constructs and disposes its own
/// <see cref="AesGcm"/>). Decrypt fails closed on any tag mismatch.
///
/// <para><b>Nonce budget (§3a):</b> a random 96-bit nonce has a birthday bound — the <c>IKeyProvider</c> must
/// rotate a key well before ~2³² encryptions under it. This cipher is the per-message primitive; the count-aware
/// rotation lives in the key provider.</para>
/// </summary>
public sealed class AesGcmFieldCipher : IFieldCipher
{
    /// <summary>96-bit nonce — the GCM standard / interoperable size.</summary>
    public const int NonceSize = 12;

    /// <summary>128-bit authentication tag — the maximum / strongest GCM tag.</summary>
    public const int TagSize = 16;

    /// <summary>256-bit key — AES-256.</summary>
    public const int KeySize = 32;

    public FieldCipherEnvelope Encrypt(ReadOnlySpan<byte> plaintext, FieldDataKey key)
    {
        var keyBytes = key.Key.Span;
        if (keyBytes.Length != KeySize)
            throw new ArgumentException($"A field data key must be {KeySize} bytes (AES-256); got {keyBytes.Length}.", nameof(key));

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(keyBytes, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new FieldCipherEnvelope(key.KeyId, nonce, ciphertext, tag);
    }

    public byte[] Decrypt(FieldCipherEnvelope envelope, FieldDataKey key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var keyBytes = key.Key.Span;
        if (keyBytes.Length != KeySize)
            throw new ArgumentException($"A field data key must be {KeySize} bytes (AES-256); got {keyBytes.Length}.", nameof(key));

        var plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(keyBytes, TagSize);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext);
        }
        catch (CryptographicException)
        {
            // Tag mismatch: wrong key, tampered value, or a crypto-shredded key. Fail closed.
            throw new FieldDecryptionException(envelope.KeyId);
        }
        return plaintext;
    }
}
