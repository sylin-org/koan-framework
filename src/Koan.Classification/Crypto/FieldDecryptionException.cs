namespace Koan.Classification.Crypto;

/// <summary>
/// Thrown when an encrypted field value cannot be authenticated/decrypted (ARCH-0098 §3) — a wrong key, a
/// tampered envelope, or a key that has been <b>crypto-shredded</b> (the erasure-certificate case: the data is
/// intentionally unrecoverable). The read-reverse decides the policy (fail closed vs surface a tombstone); the
/// cipher never returns unauthenticated plaintext.
/// </summary>
public sealed class FieldDecryptionException : Exception
{
    /// <summary>The key id the envelope referenced (its owning tenant's key), for diagnostics — not the key itself.</summary>
    public string KeyId { get; }

    public FieldDecryptionException(string keyId)
        : base($"Field value could not be decrypted under key '{keyId}' (wrong key, tampered value, or a destroyed key).")
        => KeyId = keyId;
}
