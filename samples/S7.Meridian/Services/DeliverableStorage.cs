using Koan.Storage.Abstractions;
using Koan.Samples.Meridian.Infrastructure;

namespace Koan.Samples.Meridian.Services;

public interface IDeliverableStorage
{
    Task<string> StoreAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default);
}

public sealed class DeliverableStorage : IDeliverableStorage
{
    private readonly IStorageService _storage;
    private readonly DeliverableStorageOptions _options;

    public DeliverableStorage(IStorageService storage, DeliverableStorageOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<string> StoreAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        var key = $"{_options.Prefix}{Guid.CreateVersion7()}{extension}";
        await _storage.PutAsync(_options.Profile, _options.Container, key, content, contentType, ct);
        return key;
    }
}
