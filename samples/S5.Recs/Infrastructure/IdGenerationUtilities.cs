using System.Security.Cryptography;
using System.Text;

namespace S5.Recs.Infrastructure;

public static class IdGenerationUtilities
{
    /// <summary>
    /// Generates a deterministic SHA512-based ID from a composite key.
    /// Provides collision-resistant, deterministic IDs for imported content.
    /// </summary>
    /// <param name="composite">The composite key string</param>
    /// <returns>Base64-encoded SHA512 hash (88 characters)</returns>
    public static string ComputeSHA512Hash(string composite)
    {
        if (string.IsNullOrEmpty(composite))
            throw new ArgumentException("Composite key cannot be null or empty", nameof(composite));

        using var sha512 = SHA512.Create();
        var bytes = Encoding.UTF8.GetBytes(composite);
        var hashBytes = sha512.ComputeHash(bytes);

        // Use Base64 encoding for URL-safe, shorter representation
        return Convert.ToBase64String(hashBytes)
            .Replace('+', '-')  // Make URL-safe
            .Replace('/', '_')  // Make URL-safe
            .TrimEnd('=');      // Remove padding
    }

    /// <summary>
    /// Generates a deterministic media ID from provider information.
    /// Format: SHA512("{providerCode}:{externalId}:{mediaTypeId}")
    /// </summary>
    /// <param name="providerCode">Provider code (e.g., "anilist", "mal")</param>
    /// <param name="externalId">Provider's native ID</param>
    /// <param name="mediaTypeId">Media type ID</param>
    /// <returns>Deterministic SHA512-based ID</returns>
    public static string GenerateMediaId(string providerCode, string externalId, string mediaTypeId)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
            throw new ArgumentException("Provider code cannot be null or empty", nameof(providerCode));
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("External ID cannot be null or empty", nameof(externalId));
        if (string.IsNullOrWhiteSpace(mediaTypeId))
            throw new ArgumentException("Media type ID cannot be null or empty", nameof(mediaTypeId));

        var composite = $"{providerCode.ToLowerInvariant()}:{externalId}:{mediaTypeId}";
        return ComputeSHA512Hash(composite);
    }

    /// <summary>
    /// Generates a deterministic library entry ID from user and media IDs.
    /// Format: SHA512("{userId}:{mediaId}")
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="mediaId">Media ID</param>
    /// <returns>Deterministic SHA512-based ID</returns>
    public static string GenerateLibraryEntryId(string userId, string mediaId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(mediaId))
            throw new ArgumentException("Media ID cannot be null or empty", nameof(mediaId));

        var composite = $"{userId}:{mediaId}";
        return ComputeSHA512Hash(composite);
    }
}