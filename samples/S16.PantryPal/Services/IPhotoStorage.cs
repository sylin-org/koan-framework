using Koan.Storage.Abstractions;

namespace S16.PantryPal.Services;

/// <summary>
/// Thin abstraction over Koan.Storage for pantry photo persistence.
/// Keeps controller logic oblivious to profile/container routing and future provider changes.
/// </summary>
public interface IPhotoStorage
{
    /// <summary>Stores a new photo stream, generating a storage key. Returns the key.</summary>
    Task<string> StoreAsync(Stream content, string originalFileName, string? contentType, CancellationToken ct = default);

    /// <summary>Opens a stored photo for reading.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
}

public sealed class PhotoStorage : IPhotoStorage
{
    private readonly IStorageService _storage;
    private readonly PhotoStorageOptions _options;

    public PhotoStorage(IStorageService storage, PhotoStorageOptions options)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<string> StoreAsync(Stream content, string originalFileName, string? contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName);
        var key = $"{_options.Prefix}{Guid.CreateVersion7()}{ext}"; // GUIDv7 for ordering
        await _storage.PutAsync(_options.Profile, _options.Container, key, content, contentType, ct);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        => _storage.ReadAsync(_options.Profile, _options.Container, key, ct);
}

public sealed class PhotoStorageOptions
{
    /// <summary>Storage profile name (Koan:Storage:Profiles:*)</summary>
    public string Profile { get; set; } = ""; // empty -> rely on DefaultProfile or single profile fallback
    /// <summary>Logical container/folder/bucket depending on provider.</summary>
    public string Container { get; set; } = "pantry-photos";
    /// <summary>Key prefix to namespace objects.</summary>
    public string Prefix { get; set; } = "photos/";
}
