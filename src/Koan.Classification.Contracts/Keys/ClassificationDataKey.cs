namespace Koan.Classification;

/// <summary>
/// One field-encryption data key. The provider owns key-material lifetime and zeroing; consumers must never log or
/// persist <see cref="Key"/>.
/// </summary>
/// <param name="KeyId">Stable opaque identity embedded in protected field envelopes.</param>
/// <param name="Key">The 256-bit AES key material.</param>
public readonly record struct ClassificationDataKey(string KeyId, ReadOnlyMemory<byte> Key);
