using Koan.Core.Capabilities;

namespace Koan.Storage.Abstractions;

public interface IStorageProvider : IDescribesCapabilities
{
    string Name { get; }
    StorageProviderPlacement Placement { get; }

    Task Write(string container, string key, Stream content, string? contentType, CancellationToken ct = default);
    Task<Stream> OpenRead(string container, string key, CancellationToken ct = default);
    Task<(Stream Stream, long? Length)> OpenReadRange(string container, string key, long? from, long? to, CancellationToken ct = default);
    Task<bool> Delete(string container, string key, CancellationToken ct = default);
    Task<bool> Exists(string container, string key, CancellationToken ct = default);
}
