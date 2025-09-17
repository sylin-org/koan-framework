namespace Koan.Storage.Abstractions;

public interface IListOperations
{
    /// <summary>
    /// Lists objects in a container with optional prefix filtering
    /// </summary>
    /// <param name="container">Container to list objects from</param>
    /// <param name="prefix">Optional prefix to filter objects (e.g., "backups/")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Enumerable of object information</returns>
    IAsyncEnumerable<StorageObjectInfo> ListObjectsAsync(string container, string? prefix = null, CancellationToken ct = default);
}

/// <summary>
/// Lightweight object information for listing operations
/// </summary>
public record StorageObjectInfo(
    string Key,
    long Size,
    DateTimeOffset LastModified,
    string? ContentType = null,
    string? ETag = null
);