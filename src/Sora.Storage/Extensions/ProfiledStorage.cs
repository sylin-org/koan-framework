using Sora.Storage.Abstractions;
using Newtonsoft.Json;

namespace Sora.Storage.Extensions;

public readonly struct ProfiledStorage
{
    private readonly IStorageService _svc;
    public string Profile { get; }
    public string Container { get; }

    public ProfiledStorage(IStorageService svc, string profile, string container)
    {
        _svc = svc;
        Profile = profile;
        Container = container;
    }

    // Mirror a few common helpers for fluent DX
    public Task<StorageObject> CreateTextFile(string key, string content, string? contentType = "text/plain; charset=utf-8", CancellationToken ct = default)
        => _svc.CreateTextFile(key, content, contentType, Profile, Container, ct);

    public Task<StorageObject> CreateJson<T>(string key, T value, JsonSerializerSettings? options = null, string contentType = "application/json; charset=utf-8", CancellationToken ct = default)
        => _svc.CreateJson(key, value, options, Profile, Container, contentType, ct);

    public Task<StorageObject> CreateJson(string key, string json, CancellationToken ct = default)
        => _svc.CreateJson(key, json, Profile, Container, ct: ct);

    public Task<StorageObject> Create(string key, ReadOnlyMemory<byte> bytes, string? contentType = "application/octet-stream", CancellationToken ct = default)
        => _svc.Create(key, bytes, contentType, Profile, Container, ct);

    public Task<StorageObject> Onboard(string key, Stream content, string? contentType = null, CancellationToken ct = default)
        => _svc.Onboard(key, content, contentType, Profile, Container, ct);

    public Task<StorageObject> OnboardFile(string filePath, string? key = null, string? contentType = null, CancellationToken ct = default)
        => _svc.OnboardFile(filePath, key, contentType, Profile, Container, ct);

    public Task<StorageObject> OnboardUrl(Uri uri, string? key = null, string? contentType = null, HttpClient? http = null, long? maxBytes = null, CancellationToken ct = default)
        => _svc.OnboardUrl(uri, key, contentType, http, Profile, Container, maxBytes, ct);

    // Transfer helpers
    public Task<StorageObject> CopyTo(string key, string targetProfile, string? targetContainer = null, CancellationToken ct = default)
        => _svc.CopyTo(Profile, Container, key, targetProfile, targetContainer, ct);

    public Task<StorageObject> MoveTo(string key, string targetProfile, string? targetContainer = null, CancellationToken ct = default)
        => _svc.MoveTo(Profile, Container, key, targetProfile, targetContainer, ct);
}