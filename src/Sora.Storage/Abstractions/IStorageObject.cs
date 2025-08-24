namespace Sora.Storage;

using Sora.Data.Abstractions;

public interface IStorageObject : IEntity<string>
{
    string Key { get; }
    string? Name { get; }
    string? ContentType { get; }
    long Size { get; }
    string? ContentHash { get; }
    DateTimeOffset CreatedAt { get; }
    DateTimeOffset? UpdatedAt { get; }
    string? Provider { get; }
    string? Container { get; }
    IReadOnlyDictionary<string, string>? Tags { get; }
}
