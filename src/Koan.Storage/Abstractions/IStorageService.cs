namespace Koan.Storage.Abstractions;

public interface IStorageService
{
    Task<StorageObject> PutAsync(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default);
    Task<Stream> ReadAsync(string profile, string container, string key, CancellationToken ct = default);
    Task<(Stream Stream, long? Length)> ReadRangeAsync(string profile, string container, string key, long? from, long? to, CancellationToken ct = default);
    Task<bool> DeleteAsync(string profile, string container, string key, CancellationToken ct = default);

    // Nice-to-haves to avoid full reads and orchestrate transfers
    Task<bool> ExistsAsync(string profile, string container, string key, CancellationToken ct = default);
    Task<ObjectStat?> HeadAsync(string profile, string container, string key, CancellationToken ct = default);

    // Orchestrated copy across profiles (streams when server-side copy is unavailable); optionally deletes source
    Task<StorageObject> TransferToProfileAsync(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default);

    // Presigned URLs when provider supports it
    Task<Uri> PresignReadAsync(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default);
    Task<Uri> PresignWriteAsync(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);

    // Container listing when provider supports it
    IAsyncEnumerable<StorageObjectInfo> ListObjectsAsync(string profile, string container, string? prefix = null, CancellationToken ct = default);
}
