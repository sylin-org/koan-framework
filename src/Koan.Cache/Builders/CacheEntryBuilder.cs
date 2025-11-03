using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Stores;

namespace Koan.Cache.Stores.Internal;

internal sealed class CacheEntryBuilder<T> : ICacheEntryBuilder<T>
{
    private readonly CacheClient _client;
    private CacheEntryOptions _options;

    public CacheEntryBuilder(CacheClient client, CacheKey key, CacheEntryOptions options)
    {
        _client = client;
        Key = key;
        _options = options;
    }

    public CacheKey Key { get; }

    public CacheEntryOptions Options => _options;

    public ICacheEntryBuilder<T> WithOptions(Func<CacheEntryOptions, CacheEntryOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        _options = configure(_options);
        return this;
    }

    public ICacheEntryBuilder<T> WithAbsoluteTtl(TimeSpan ttl)
    {
        _options = _options with { AbsoluteTtl = ttl };
        return this;
    }

    public ICacheEntryBuilder<T> WithSlidingTtl(TimeSpan ttl)
    {
        _options = _options with { SlidingTtl = ttl };
        return this;
    }

    public ICacheEntryBuilder<T> AllowStaleFor(TimeSpan duration)
    {
        _options = _options with { AllowStaleFor = duration };
        return this;
    }

    public ICacheEntryBuilder<T> WithTags(params string[] tags)
    {
        _options = _options.WithTags(tags);
        return this;
    }

    public ICacheEntryBuilder<T> WithContentKind(CacheContentKind kind)
    {
        _options = _options with { ContentKind = kind };
        return this;
    }

    public ICacheEntryBuilder<T> PublishInvalidation(bool value = true)
    {
        _options = _options with { ForcePublishInvalidation = value };
        return this;
    }

    public ICacheEntryBuilder<T> WithConsistency(CacheConsistencyMode mode)
    {
        _options = _options with { Consistency = mode };
        return this;
    }

    public ValueTask<T?> GetAsync(CancellationToken ct)
        => _client.GetAsync<T>(Key, _options, ct);

    public ValueTask<T?> GetOrAddAsync(Func<CancellationToken, ValueTask<T?>> valueFactory, CancellationToken ct)
        => _client.GetOrAddAsync(Key, valueFactory, _options, ct);

    public ValueTask SetAsync(T value, CancellationToken ct)
        => _client.SetAsync(Key, value, _options, ct);

    public async ValueTask RemoveAsync(CancellationToken ct)
        => await _client.RemoveAsync(Key, ct);

    public ValueTask TouchAsync(CancellationToken ct)
        => _client.TouchAsync(Key, _options, ct);

    public ValueTask<bool> Exists(CancellationToken ct)
        => _client.ExistsAsync(Key, _options, ct);
}
