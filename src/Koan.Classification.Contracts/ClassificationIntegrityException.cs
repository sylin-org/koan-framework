namespace Koan.Classification;

/// <summary>Thrown when a protected field envelope is malformed or fails authenticated decryption.</summary>
public sealed class ClassificationIntegrityException : Exception
{
    public ClassificationIntegrityException(string message)
        : base(message)
    {
    }

    public ClassificationIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
