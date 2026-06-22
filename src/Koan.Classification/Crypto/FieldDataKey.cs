namespace Koan.Classification.Crypto;

/// <summary>
/// A single field-encryption data key — 256-bit AES key material plus its stable <see cref="KeyId"/> (ARCH-0098
/// §3). The <see cref="KeyId"/> is embedded in every <see cref="FieldCipherEnvelope"/> so a value encrypted under
/// a now-retired key still resolves to the right key on decrypt (rotation survival). Produced by the
/// <c>IKeyProvider</c> (per-tenant, env-tiered); consumed by <see cref="IFieldCipher"/>. Holds key material — do
/// not log it; the provider owns its lifetime + zeroing.
/// </summary>
/// <param name="KeyId">The stable key identity embedded in ciphertext envelopes (maps to its owning tenant).</param>
/// <param name="Key">The 256-bit (32-byte) AES key material.</param>
public readonly record struct FieldDataKey(string KeyId, ReadOnlyMemory<byte> Key);
