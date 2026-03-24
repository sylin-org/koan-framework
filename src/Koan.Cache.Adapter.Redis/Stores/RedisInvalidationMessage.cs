using System;

namespace Koan.Cache.Adapter.Redis.Stores;

internal sealed record RedisInvalidationMessage
{
    public string Key { get; init; } = "";
    public string NamespacedKey { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public string? Region { get; init; }
    public string? ScopeId { get; init; }
}
