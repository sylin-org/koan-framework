namespace Koan.Storage.Abstractions;

public interface IStorageService
{
    Task<StorageObject> Put(string profile, string container, string key, Stream content, string? contentType, CancellationToken ct = default);
    Task<Stream> Read(string profile, string container, string key, CancellationToken ct = default);
    Task<(Stream Stream, long? Length)> ReadRange(string profile, string container, string key, long? from, long? to, CancellationToken ct = default);
    Task<bool> Delete(string profile, string container, string key, CancellationToken ct = default);

    // Nice-to-haves to avoid full reads and orchestrate transfers
    Task<bool> Exists(string profile, string container, string key, CancellationToken ct = default);
    Task<ObjectStat?> Head(string profile, string container, string key, CancellationToken ct = default);

    // Orchestrated copy across profiles (streams when server-side copy is unavailable); optionally deletes source
    Task<StorageObject> TransferToProfile(string sourceProfile, string sourceContainer, string key, string targetProfile, string? targetContainer = null, bool deleteSource = false, CancellationToken ct = default);

    // Presigned URLs when provider supports it
    Task<Uri> PresignRead(string profile, string container, string key, TimeSpan expiry, CancellationToken ct = default);
    Task<Uri> PresignWrite(string profile, string container, string key, TimeSpan expiry, string? contentType = null, CancellationToken ct = default);

    // Container listing when provider supports it
    IAsyncEnumerable<StorageObjectInfo> ListObjects(string profile, string container, string? prefix = null, CancellationToken ct = default);
}
