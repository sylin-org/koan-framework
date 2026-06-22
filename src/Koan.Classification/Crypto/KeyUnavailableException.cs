namespace Koan.Classification.Crypto;

/// <summary>
/// Thrown by <see cref="IKeyProvider.GetForDecrypt"/> when a key id is unknown or has been crypto-shredded
/// (ARCH-0098 §3a). Distinct from <see cref="FieldDecryptionException"/> (a present-but-wrong key / tampered
/// value): here the key is intentionally <b>gone</b>. The read-reverse treats both as "cannot recover" and fails
/// closed (or surfaces a tombstone), never plaintext.
/// </summary>
public sealed class KeyUnavailableException : Exception
{
    /// <summary>The key id that could not be resolved.</summary>
    public string KeyId { get; }

    public KeyUnavailableException(string keyId)
        : base($"Field-encryption key '{keyId}' is unavailable (unknown or crypto-shredded).")
        => KeyId = keyId;
}
