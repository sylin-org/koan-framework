using Koan.Storage;
using Koan.Storage.Abstractions;
using Koan.Samples.Meridian.Infrastructure;

namespace Koan.Samples.Meridian.Services;

public interface IDocumentStorage
{
    Task<string> Store(Stream content, string fileName, string? contentType, CancellationToken ct = default);
    Task<Stream> OpenRead(string storageKey, CancellationToken ct = default);
    Task Delete(string storageKey, CancellationToken ct = default);
}

public sealed class DocumentStorageOptions
{
    public string Profile { get; set; } = MeridianConstants.StorageProfile;
    public string Container { get; set; } = MeridianConstants.StorageContainer;
    public string Prefix { get; set; } = "documents/";
}

public sealed class DocumentStorage : IDocumentStorage
{
    private readonly IStorageService _storage;
    private readonly DocumentStorageOptions _options;

    public DocumentStorage(IStorageService storage, DocumentStorageOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<string> Store(Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        var key = $"{_options.Prefix}{Guid.CreateVersion7()}{extension}";
        await _storage.Put(_options.Profile, _options.Container, key, content, contentType, ct);
        return key;
    }

    public Task<Stream> OpenRead(string storageKey, CancellationToken ct = default)
        => _storage.Read(_options.Profile, _options.Container, storageKey, ct);

    public Task Delete(string storageKey, CancellationToken ct = default)
        => _storage.Delete(_options.Profile, _options.Container, storageKey, ct);
}
