namespace Sora.Storage;

public record StorageProviderCapabilities(
    bool SupportsSequentialRead,
    bool SupportsSeek,
    bool SupportsPresignedRead,
    bool SupportsServerSideCopy
);

public interface IStorageProvider
{
    string Name { get; }
    StorageProviderCapabilities Capabilities { get; }

    Task WriteAsync(string container, string key, Stream content, string? contentType, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string container, string key, CancellationToken ct = default);
    Task<(Stream Stream, long? Length)> OpenReadRangeAsync(string container, string key, long? from, long? to, CancellationToken ct = default);
    Task<bool> DeleteAsync(string container, string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string container, string key, CancellationToken ct = default);
}

public interface IStatOperations
{
    Task<ObjectStat?> HeadAsync(string container, string key, CancellationToken ct = default);
}

public interface IPresignOperations
{
    Task<Uri> PresignReadAsync(string container, string key, TimeSpan expiry, CancellationToken ct = default);
    Task<Uri> PresignWriteAsync(string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);
}

public interface IServerSideCopy
{
    Task<bool> CopyAsync(string sourceContainer, string sourceKey, string targetContainer, string targetKey, CancellationToken ct = default);
}

public sealed record ObjectStat(long? Length, string? ContentType, DateTimeOffset? LastModified, string? ETag);
