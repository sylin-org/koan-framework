namespace Sora.Storage.Abstractions;

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