namespace Koan.Classification.Crypto;

/// <summary>
/// Stateless authenticated encryption for a single field value (ARCH-0098 §3). One implementation —
/// <see cref="AesGcmFieldCipher"/> (AES-256-GCM). Encrypt produces a self-describing
/// <see cref="FieldCipherEnvelope"/> (key id + nonce + tag + ciphertext); decrypt fails closed (throws
/// <see cref="ClassificationIntegrityException"/>) on a wrong key or tampered value and never returns garbage.
/// </summary>
internal interface IFieldCipher
{
    /// <summary>Encrypt <paramref name="plaintext"/> under <paramref name="key"/>, returning a fresh-nonce envelope.</summary>
    FieldCipherEnvelope Encrypt(ReadOnlySpan<byte> plaintext, ClassificationDataKey key);

    /// <summary>
    /// Decrypt and authenticate <paramref name="envelope"/> with <paramref name="key"/>. Throws
    /// <see cref="ClassificationIntegrityException"/> if the tag does not verify.
    /// </summary>
    byte[] Decrypt(FieldCipherEnvelope envelope, ClassificationDataKey key);
}
