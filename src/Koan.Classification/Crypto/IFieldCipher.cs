namespace Koan.Classification.Crypto;

/// <summary>
/// Stateless authenticated encryption for a single field value (ARCH-0098 §3). One implementation —
/// <see cref="AesGcmFieldCipher"/> (AES-256-GCM). Encrypt produces a self-describing
/// <see cref="FieldCipherEnvelope"/> (key id + nonce + tag + ciphertext); decrypt fails closed (throws
/// <see cref="FieldDecryptionException"/>) on a wrong key, a tampered value, or a destroyed key — never returns
/// garbage. The cipher does not own keys: the <c>IKeyProvider</c> resolves the <see cref="FieldDataKey"/> (the
/// active key for encrypt, the envelope's owning-tenant key for decrypt).
/// </summary>
public interface IFieldCipher
{
    /// <summary>Encrypt <paramref name="plaintext"/> under <paramref name="key"/>, returning a fresh-nonce envelope.</summary>
    FieldCipherEnvelope Encrypt(ReadOnlySpan<byte> plaintext, FieldDataKey key);

    /// <summary>
    /// Decrypt and authenticate <paramref name="envelope"/> with <paramref name="key"/>. Throws
    /// <see cref="FieldDecryptionException"/> if the tag does not verify (wrong key / tamper / shredded key).
    /// </summary>
    byte[] Decrypt(FieldCipherEnvelope envelope, FieldDataKey key);
}
