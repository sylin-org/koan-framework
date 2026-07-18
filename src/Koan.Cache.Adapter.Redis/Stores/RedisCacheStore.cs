using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Capabilities;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Serialization;
using Koan.Cache.Adapter.Redis.Infrastructure;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Core.Capabilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Stores;

/// <summary>
/// Distributed L2 cache store backed by Redis. Pure storage; Cache owns invalidation meaning and
/// the layered Redis Communication capability owns physical node broadcast.
/// </summary>
[ProviderPriority(Constants.ProviderPriority)]
public sealed class RedisCacheStore : ICacheStore
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly RedisCacheAdapterOptions _options;
    private readonly ILogger<RedisCacheStore> _logger;
    private readonly string _instancePrefix;
    private readonly string _keyPrefix;
    private readonly string _tagPrefix;

    public RedisCacheStore(
        IConnectionMultiplexer multiplexer,
        IOptions<RedisCacheAdapterOptions> options,
        ILogger<RedisCacheStore> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _multiplexer.GetDatabase(_options.Database >= 0 ? _options.Database : -1);
        _instancePrefix = NormalizePrefix(_options.InstanceName);
        _keyPrefix = NormalizePrefix(string.IsNullOrWhiteSpace(_options.KeyPrefix) ? Constants.DefaultKeyPrefix : _options.KeyPrefix!);
        _tagPrefix = NormalizePrefix(string.IsNullOrWhiteSpace(_options.TagPrefix) ? Constants.DefaultTagPrefix : _options.TagPrefix!);
    }

    public string Name => Constants.ProviderId;

    public CacheStorePlacement Placement => CacheStorePlacement.Remote;

    public void Describe(ICapabilities caps)
        => caps.Add(CacheCaps.Tags)
            .Add(CacheCaps.SlidingExpiration)
            .Add(CacheCaps.BoundedStaleServing)
            .Add(CacheCaps.BinaryPayload);

    public async ValueTask<CacheFetchResult> Fetch(CacheKey key, CacheReadOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var raw = await _database.StringGetAsync(redisKey).ConfigureAwait(false);
        if (!raw.HasValue)
            return CacheFetchResult.Miss(new CacheEntryOptions());

        RedisCacheEnvelope envelope;
        try
        {
            envelope = RedisCacheJsonConverter.DeserializeEnvelope(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry for {CacheKey}", key.Value);
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        var cachedOptions = envelope.Options.ToOptions();
        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = envelope.AbsoluteExpirationUtcTicks.HasValue
            ? new DateTimeOffset(envelope.AbsoluteExpirationUtcTicks.Value, TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var staleUntil = envelope.StaleUntilUtcTicks.HasValue
            ? new DateTimeOffset(envelope.StaleUntilUtcTicks.Value, TimeSpan.Zero)
            : (DateTimeOffset?)null;

        if (staleUntil is { } finalExpiry && finalExpiry <= now)
        {
            await Evict(redisKey, envelope.Options.Tags).ConfigureAwait(false);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        // Per ARCH-0078: read-side AllowStaleFor is the master signal. Past the absolute TTL,
        // the caller must have explicitly opted into staleness for this read or the store
        // treats it as Miss.
        var absoluteExpired = absoluteExpiration is { } abs && abs <= now;
        if (absoluteExpired && options.AllowStaleFor is null)
        {
            await Evict(redisKey, envelope.Options.Tags).ConfigureAwait(false);
            return CacheFetchResult.Miss(new CacheEntryOptions());
        }

        if (cachedOptions.SlidingTtl is { } && !absoluteExpired)
            await RefreshSlidingTtl(redisKey, envelope, cachedOptions, now).ConfigureAwait(false);

        return CacheFetchResult.HitResult(envelope.Value.ToCacheValue(), cachedOptions, absoluteExpiration, staleUntil);
    }

    public ValueTask Set(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return SetCore(key, value, options, ct, removeExisting: true);
    }

    public async ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var removed = await _database.StringGetDeleteAsync(redisKey).ConfigureAwait(false);
        if (!removed.HasValue) return false;

        try
        {
            var envelope = RedisCacheJsonConverter.DeserializeEnvelope(removed);
            await RemoveTags(redisKey, envelope.Options.Tags).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry during removal for {CacheKey}", key.Value);
        }

        return true;
    }

    public async ValueTask Touch(CacheKey key, TimeSpan? newAbsoluteTtl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var raw = await _database.StringGetAsync(redisKey).ConfigureAwait(false);
        if (!raw.HasValue) return;

        RedisCacheEnvelope envelope;
        try
        {
            envelope = RedisCacheJsonConverter.DeserializeEnvelope(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry during touch for {CacheKey}", key.Value);
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return;
        }

        var updatedOptions = envelope.Options.ToOptions() with { AbsoluteTtl = newAbsoluteTtl };
        await SetCore(key, envelope.Value.ToCacheValue(), updatedOptions.ToWriteOptions(), ct, removeExisting: true).ConfigureAwait(false);
    }

    public async ValueTask<bool> Exists(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var raw = await _database.StringGetAsync(redisKey).ConfigureAwait(false);
        if (!raw.HasValue) return false;

        try
        {
            var envelope = RedisCacheJsonConverter.DeserializeEnvelope(raw);
            var now = DateTimeOffset.UtcNow;
            var absoluteExpiration = envelope.AbsoluteExpirationUtcTicks.HasValue
                ? new DateTimeOffset(envelope.AbsoluteExpirationUtcTicks.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;
            var staleUntil = envelope.StaleUntilUtcTicks.HasValue
                ? new DateTimeOffset(envelope.StaleUntilUtcTicks.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;

            // Per ARCH-0078: Exists reports storage presence (entry within staleness ceiling).
            // Whether a Fetch surfaces a stale value is the reader's per-call opt-in.
            if (staleUntil is { } finalExpiry && finalExpiry <= now)
            {
                await Evict(redisKey, envelope.Options.Tags).ConfigureAwait(false);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry during exists check for {CacheKey}", key.Value);
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return false;
        }
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tagKey = BuildTagKey(tag);
        await Task.Yield();

        foreach (var member in _database.SetScan(tagKey))
        {
            ct.ThrowIfCancellationRequested();

            var memberValue = (RedisValue)member;
            var memberString = memberValue.ToString();
            if (string.IsNullOrEmpty(memberString))
            {
                await _database.SetRemoveAsync(tagKey, memberValue).ConfigureAwait(false);
                continue;
            }

            var redisKey = (RedisKey)memberString!;
            var value = await _database.StringGetAsync(redisKey).ConfigureAwait(false);
            if (!value.HasValue)
            {
                await _database.SetRemoveAsync(tagKey, memberValue).ConfigureAwait(false);
                continue;
            }

            RedisCacheEnvelope envelope;
            try
            {
                envelope = RedisCacheJsonConverter.DeserializeEnvelope(value);
            }
            catch
            {
                await _database.SetRemoveAsync(tagKey, memberValue).ConfigureAwait(false);
                continue;
            }

            var absoluteExpiration = envelope.AbsoluteExpirationUtcTicks.HasValue
                ? new DateTimeOffset(envelope.AbsoluteExpirationUtcTicks.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;

            yield return new TaggedCacheKey(tag, new CacheKey(envelope.Key), absoluteExpiration);
        }
    }

    private async ValueTask SetCore(CacheKey key, CacheValue value, CacheWriteOptions options, CancellationToken ct, bool removeExisting)
    {
        var redisKey = BuildRedisKey(key);
        if (removeExisting)
        {
            var existing = await _database.StringGetAsync(redisKey).ConfigureAwait(false);
            if (existing.HasValue)
            {
                try
                {
                    var existingEnvelope = RedisCacheJsonConverter.DeserializeEnvelope(existing);
                    await RemoveTags(redisKey, existingEnvelope.Options.Tags).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove previous tags for {CacheKey}", key.Value);
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = options.AbsoluteTtl.HasValue ? now.Add(options.AbsoluteTtl.Value) : (DateTimeOffset?)null;
        var staleUntil = absoluteExpiration;
        if (options.AllowStaleFor.HasValue && absoluteExpiration.HasValue)
            staleUntil = absoluteExpiration.Value.Add(options.AllowStaleFor.Value);

        var storedOptions = CacheEntryOptions.FromWriteOptions(options);
        var envelope = new RedisCacheEnvelope
        {
            Key = key.Value,
            CreatedUtcTicks = now.UtcTicks,
            AbsoluteExpirationUtcTicks = absoluteExpiration?.UtcTicks,
            StaleUntilUtcTicks = staleUntil?.UtcTicks,
            Value = CacheValueModel.FromCacheValue(value),
            Options = CacheEntryOptionsModel.FromOptions(storedOptions)
        };

        var expiry = DetermineExpiry(now, absoluteExpiration, staleUntil);
        if (expiry.HasValue && expiry.Value <= TimeSpan.Zero)
        {
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            await RemoveTags(redisKey, envelope.Options.Tags).ConfigureAwait(false);
            return;
        }

        var payload = RedisCacheJsonConverter.SerializeEnvelope(envelope);
        await _database.StringSetAsync(redisKey, payload, expiry).ConfigureAwait(false);
        await IndexTags(redisKey, envelope.Options.Tags, expiry).ConfigureAwait(false);
    }

    private async Task RefreshSlidingTtl(RedisKey redisKey, RedisCacheEnvelope envelope, CacheEntryOptions cachedOptions, DateTimeOffset now)
    {
        var sliding = cachedOptions.SlidingTtl.GetValueOrDefault();
        if (sliding <= TimeSpan.Zero) return;

        var newAbsolute = now.Add(sliding);
        DateTimeOffset? newStale = newAbsolute;
        if (cachedOptions.AllowStaleFor.HasValue)
            newStale = newAbsolute.Add(cachedOptions.AllowStaleFor.Value);

        envelope = envelope with
        {
            CreatedUtcTicks = now.UtcTicks,
            AbsoluteExpirationUtcTicks = newAbsolute.UtcTicks,
            StaleUntilUtcTicks = newStale?.UtcTicks
        };

        var expiry = DetermineExpiry(now, newAbsolute, newStale);
        var payload = RedisCacheJsonConverter.SerializeEnvelope(envelope);
        await _database.StringSetAsync(redisKey, payload, expiry).ConfigureAwait(false);
        await IndexTags(redisKey, cachedOptions.Tags, expiry).ConfigureAwait(false);
    }

    private async Task IndexTags(RedisKey redisKey, IEnumerable<string> tags, TimeSpan? expiry)
    {
        var normalized = tags?.Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (normalized.Length == 0) return;

        foreach (var tag in normalized)
        {
            var tagKey = BuildTagKey(tag);
            var keyString = redisKey.ToString();
            if (string.IsNullOrEmpty(keyString)) continue;

            var namespacedKey = (RedisValue)keyString!;
            await _database.SetAddAsync(tagKey, namespacedKey).ConfigureAwait(false);
            if (expiry.HasValue)
                await _database.KeyExpireAsync(tagKey, expiry).ConfigureAwait(false);
        }
    }

    private async Task RemoveTags(RedisKey redisKey, IEnumerable<string> tags)
    {
        if (tags is null) return;

        var keyString = redisKey.ToString();
        if (string.IsNullOrEmpty(keyString)) return;

        var namespacedKey = (RedisValue)keyString!;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            var tagKey = BuildTagKey(tag.Trim());
            await _database.SetRemoveAsync(tagKey, namespacedKey).ConfigureAwait(false);
        }
    }

    private async Task Evict(RedisKey redisKey, IEnumerable<string> tags)
    {
        await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
        await RemoveTags(redisKey, tags).ConfigureAwait(false);
    }

    private static TimeSpan? DetermineExpiry(DateTimeOffset now, DateTimeOffset? absoluteExpiration, DateTimeOffset? staleUntil)
    {
        if (staleUntil.HasValue)
        {
            var ttl = staleUntil.Value - now;
            return ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero;
        }

        if (absoluteExpiration.HasValue)
        {
            var ttl = absoluteExpiration.Value - now;
            return ttl > TimeSpan.Zero ? ttl : TimeSpan.Zero;
        }

        return null;
    }

    private RedisKey BuildRedisKey(CacheKey key) => (RedisKey)($"{_keyPrefix}{_instancePrefix}{key.Value}");

    private RedisKey BuildTagKey(string tag) => (RedisKey)($"{_tagPrefix}{_instancePrefix}{tag}");

    private static string NormalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var trimmed = value.Trim();
        while (trimmed.EndsWith(":", StringComparison.Ordinal))
            trimmed = trimmed[..^1];

        return trimmed.Length == 0 ? "" : trimmed + ":";
    }
}
