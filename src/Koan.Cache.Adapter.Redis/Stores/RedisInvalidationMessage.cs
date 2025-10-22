using System;

namespace Koan.Cache.Adapter.Redis.Stores;

internal sealed record RedisInvalidationMessage
{
    public string Key { get; init; } = string.Empty;
    public string NamespacedKey { get; init; } = string.Empty;
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string? Region { get; init; }
    public string? ScopeId { get; init; }
}
