using System;
using System.Collections.Generic;
using System.Threading;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Scope;
using Koan.Cache.Stores;
using Koan.Core.Hosting.App;

namespace Koan.Cache;

public static class Cache
{
    public static ICacheClient Client
        => (ICacheClient)(AppHost.Current?.GetService(typeof(ICacheClient))
            ?? throw new InvalidOperationException("ICacheClient is not available. Ensure services.AddKoanCache() has been invoked."));

    public static ICacheEntryBuilder<T> WithJson<T>(string key)
        => Client.CreateEntry<T>(new CacheKey(key)).WithContentKind(CacheContentKind.Json);

    public static ICacheEntryBuilder<string> WithString(string key)
        => Client.CreateEntry<string>(new CacheKey(key)).WithContentKind(CacheContentKind.String);

    public static ICacheEntryBuilder<byte[]> WithBinary(string key)
        => Client.CreateEntry<byte[]>(new CacheKey(key)).WithContentKind(CacheContentKind.Binary);

    public static ICacheEntryBuilder<T> WithRecord<T>(string key)
        => Client.CreateEntry<T>(new CacheKey(key)).WithContentKind(CacheContentKind.Json);

    public static CacheScopeHandle BeginScope(string scopeId, string? region = null)
    {
        if (AppHost.Current?.GetService(typeof(ICacheClient)) is not CacheClient client)
        {
            throw new InvalidOperationException("Cache scope cannot be created because CacheClient is not registered.");
        }

        return client.BeginScope(scopeId, region);
    }

    public static ValueTask<bool> Exists(string key, CancellationToken ct = default)
    {
        var cacheKey = new CacheKey(key);
        return Client.ExistsAsync(cacheKey, new CacheEntryOptions(), ct);
    }

    public static CacheTagSet Tags(params string[] tags)
        => new(Client, tags);

    public static CacheTagSet Tags(IEnumerable<string> tags)
        => new(Client, tags ?? Array.Empty<string>());
}
