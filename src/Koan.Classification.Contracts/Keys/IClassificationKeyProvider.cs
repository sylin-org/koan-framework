namespace Koan.Classification;

/// <summary>
/// Supplies field-encryption keys for one opaque compiled segmentation scope. A production implementation owns
/// durable custody, rotation retention, and material disposal; the runtime owns scope derivation and AES mechanics.
/// </summary>
public interface IClassificationKeyProvider
{
    /// <summary>Return the active encryption key for <paramref name="scope"/>.</summary>
    ClassificationDataKey GetActiveKey(string scope);

    /// <summary>
    /// Resolve the key embedded in a stored envelope, independent of the current ambient scope.
    /// Throw <see cref="ClassificationKeyUnavailableException"/> when it cannot be recovered.
    /// </summary>
    ClassificationDataKey GetForDecrypt(string keyId);
}
