namespace Sora.Storage;

public sealed class StorageObject : IStorageObject
{
    public required string Id { get; init; }
    public required string Key { get; init; }
    public string? Name { get; init; }
    public string? ContentType { get; init; }
    public long Size { get; init; }
    public string? ContentHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Provider { get; init; }
    public string? Container { get; init; }
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
}
