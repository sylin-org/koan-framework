namespace Koan.Classification;

/// <summary>Thrown when a stored field references key material the configured provider cannot recover.</summary>
public sealed class ClassificationKeyUnavailableException : Exception
{
    public string KeyId { get; }

    public ClassificationKeyUnavailableException(string keyId)
        : base($"Classification key '{keyId}' is unavailable. Restore the owning key provider/material before reading this value.")
        => KeyId = keyId;
}
