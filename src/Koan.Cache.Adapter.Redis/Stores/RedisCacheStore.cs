using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Stores;

internal sealed class RedisCacheStore : ICacheStore
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly ISubscriber? _subscriber;
    private readonly RedisCacheAdapterOptions _options;
    private readonly ILogger<RedisCacheStore> _logger;
    private readonly string _instancePrefix;
    private readonly string _keyPrefix;
    private readonly string _tagPrefix;
    private readonly RedisChannel _channel;

    internal RedisChannel Channel => _channel;

    public RedisCacheStore(
        IConnectionMultiplexer multiplexer,
        IOptions<RedisCacheAdapterOptions> options,
        ILogger<RedisCacheStore> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _multiplexer.GetDatabase(_options.Database >= 0 ? _options.Database : -1);
        _subscriber = _options.EnablePubSubInvalidation ? _multiplexer.GetSubscriber() : null;

        _instancePrefix = NormalizePrefix(_options.InstanceName);
        _keyPrefix = NormalizePrefix(string.IsNullOrWhiteSpace(_options.KeyPrefix) ? "cache" : _options.KeyPrefix!);
        _tagPrefix = NormalizePrefix(string.IsNullOrWhiteSpace(_options.TagPrefix) ? "cache:tag" : _options.TagPrefix!);
        _channel = new RedisChannel(string.IsNullOrWhiteSpace(_options.ChannelName) ? "koan-cache" : _options.ChannelName!, RedisChannel.PatternMode.Literal);
    }

    public string ProviderName => "redis";

    public CacheCapabilities Capabilities { get; } = new(
        SupportsBinary: true,
        SupportsPubSubInvalidation: true,
        SupportsCompareExchange: false,
        SupportsRegionScoping: true,
        Hints: new HashSet<string>(new[] { "tags", "stale-while-revalidate", "singleflight", "pubsub" }, StringComparer.OrdinalIgnoreCase));

    public async ValueTask<CacheFetchResult> FetchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var raw = await _database.StringGetAsync(redisKey);
        if (!raw.HasValue)
        {
            return CacheFetchResult.Miss(options);
        }

        RedisCacheEnvelope envelope;
        try
        {
            envelope = RedisCacheJsonConverter.DeserializeEnvelope(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry for {CacheKey}", key.Value);
            await _database.KeyDeleteAsync(redisKey);
            return CacheFetchResult.Miss(options);
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
            await EvictAsync(redisKey, envelope.Options.Tags);
            return CacheFetchResult.Miss(options);
        }

        var absoluteExpired = absoluteExpiration is { } abs && abs <= now;
        if (absoluteExpired && !_options.EnableStaleWhileRevalidate)
        {
            await EvictAsync(redisKey, envelope.Options.Tags);
            return CacheFetchResult.Miss(options);
        }

        if (absoluteExpired && _options.EnableStaleWhileRevalidate && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Serving stale Redis cache entry for {CacheKey}", key.Value);
        }

        if (cachedOptions.SlidingTtl is { } sliding && !absoluteExpired)
        {
            await RefreshSlidingTtlAsync(redisKey, key, envelope, cachedOptions, now);
        }

        return CacheFetchResult.HitResult(envelope.Value.ToCacheValue(), cachedOptions, absoluteExpiration, staleUntil);
    }

    public ValueTask SetAsync(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return SetCoreAsync(key, value, options, ct, removeExisting: true);
    }

    public async ValueTask<bool> RemoveAsync(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var removed = await _database.StringGetDeleteAsync(redisKey);
        if (!removed.HasValue)
        {
            return false;
        }

        try
        {
            var envelope = RedisCacheJsonConverter.DeserializeEnvelope(removed);
            await RemoveTagsAsync(redisKey, envelope.Options.Tags);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry during removal for {CacheKey}", key.Value);
        }

        return true;
    }

    public async ValueTask TouchAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var raw = await _database.StringGetAsync(redisKey);
        if (!raw.HasValue)
        {
            return;
        }

        RedisCacheEnvelope envelope;
        try
        {
            envelope = RedisCacheJsonConverter.DeserializeEnvelope(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry during touch for {CacheKey}", key.Value);
            await _database.KeyDeleteAsync(redisKey);
            return;
        }

        await SetCoreAsync(key, envelope.Value.ToCacheValue(), envelope.Options.ToOptions(), ct, removeExisting: true);
    }

    public async ValueTask<bool> ExistsAsync(CacheKey key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = BuildRedisKey(key);
        var raw = await _database.StringGetAsync(redisKey);
        if (!raw.HasValue)
        {
            return false;
        }

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

            if (staleUntil is { } finalExpiry && finalExpiry <= now)
            {
                await EvictAsync(redisKey, envelope.Options.Tags);
                return false;
            }

            if (!_options.EnableStaleWhileRevalidate && absoluteExpiration is { } absolute && absolute <= now)
            {
                await EvictAsync(redisKey, envelope.Options.Tags);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cache entry during exists check for {CacheKey}", key.Value);
            await _database.KeyDeleteAsync(redisKey);
            return false;
        }
    }

    public ValueTask PublishInvalidationAsync(CacheKey key, CacheEntryOptions options, CancellationToken ct)
    {
        if (!_options.EnablePubSubInvalidation || _subscriber is null)
        {
            return ValueTask.CompletedTask;
        }

        ct.ThrowIfCancellationRequested();

        var payload = RedisCacheJsonConverter.SerializeInvalidation(new RedisInvalidationMessage
        {
            Key = key.Value,
            NamespacedKey = BuildRedisKey(key).ToString(),
            Tags = options.Tags is { Count: > 0 } publishTags
                ? publishTags.ToArray()
                : Array.Empty<string>(),
            Region = options.Region,
            ScopeId = options.ScopeId
        });

        return new ValueTask(_subscriber.PublishAsync(_channel, payload, CommandFlags.FireAndForget));
    }

    public async IAsyncEnumerable<TaggedCacheKey> EnumerateByTagAsync(string tag, [EnumeratorCancellation] CancellationToken ct)
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
                await _database.SetRemoveAsync(tagKey, memberValue);
                continue;
            }

            var redisKey = (RedisKey)memberString!;
            var value = await _database.StringGetAsync(redisKey);
            if (!value.HasValue)
            {
                await _database.SetRemoveAsync(tagKey, memberValue);
                continue;
            }

            RedisCacheEnvelope envelope;
            try
            {
                envelope = RedisCacheJsonConverter.DeserializeEnvelope(value);
            }
            catch
            {
                await _database.SetRemoveAsync(tagKey, memberValue);
                continue;
            }

            var absoluteExpiration = envelope.AbsoluteExpirationUtcTicks.HasValue
                ? new DateTimeOffset(envelope.AbsoluteExpirationUtcTicks.Value, TimeSpan.Zero)
                : (DateTimeOffset?)null;

            yield return new TaggedCacheKey(tag, new CacheKey(envelope.Key), absoluteExpiration);
        }
    }

    private async ValueTask SetCoreAsync(CacheKey key, CacheValue value, CacheEntryOptions options, CancellationToken ct, bool removeExisting)
    {
        var redisKey = BuildRedisKey(key);
        if (removeExisting)
        {
            var existing = await _database.StringGetAsync(redisKey);
            if (existing.HasValue)
            {
                try
                {
                    var existingEnvelope = RedisCacheJsonConverter.DeserializeEnvelope(existing);
                    await RemoveTagsAsync(redisKey, existingEnvelope.Options.Tags);
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
        {
            staleUntil = absoluteExpiration.Value.Add(options.AllowStaleFor.Value);
        }

        var envelope = new RedisCacheEnvelope
        {
            Key = key.Value,
            CreatedUtcTicks = now.UtcTicks,
            AbsoluteExpirationUtcTicks = absoluteExpiration?.UtcTicks,
            StaleUntilUtcTicks = staleUntil?.UtcTicks,
            Value = CacheValueModel.FromCacheValue(value),
            Options = CacheEntryOptionsModel.FromOptions(options)
        };

        var expiry = DetermineExpiry(now, absoluteExpiration, staleUntil);
        if (expiry.HasValue && expiry.Value <= TimeSpan.Zero)
        {
            await _database.KeyDeleteAsync(redisKey);
            await RemoveTagsAsync(redisKey, envelope.Options.Tags);
            return;
        }

        var payload = RedisCacheJsonConverter.SerializeEnvelope(envelope);
        await _database.StringSetAsync(redisKey, payload, expiry);
        await IndexTagsAsync(redisKey, envelope.Options.Tags, expiry);
    }

    private async Task RefreshSlidingTtlAsync(RedisKey redisKey, CacheKey key, RedisCacheEnvelope envelope, CacheEntryOptions cachedOptions, DateTimeOffset now)
    {
        var sliding = cachedOptions.SlidingTtl.GetValueOrDefault();
        if (sliding <= TimeSpan.Zero)
        {
            return;
        }

        var newAbsolute = now.Add(sliding);
        DateTimeOffset? newStale = newAbsolute;
        if (cachedOptions.AllowStaleFor.HasValue)
        {
            newStale = newAbsolute.Add(cachedOptions.AllowStaleFor.Value);
        }

        envelope = envelope with
        {
            CreatedUtcTicks = now.UtcTicks,
            AbsoluteExpirationUtcTicks = newAbsolute.UtcTicks,
            StaleUntilUtcTicks = newStale?.UtcTicks
        };

        var expiry = DetermineExpiry(now, newAbsolute, newStale);
        var payload = RedisCacheJsonConverter.SerializeEnvelope(envelope);
        await _database.StringSetAsync(redisKey, payload, expiry);
        await IndexTagsAsync(redisKey, cachedOptions.Tags, expiry);
    }

    internal async ValueTask HandleInvalidationMessageAsync(RedisInvalidationMessage message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var redisKey = ResolveRedisKey(message);
        await _database.KeyDeleteAsync(redisKey);
        await RemoveTagsAsync(redisKey, message.Tags ?? Array.Empty<string>());
    }

    private RedisKey ResolveRedisKey(RedisInvalidationMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.NamespacedKey))
        {
            return (RedisKey)message.NamespacedKey!;
        }

        return BuildRedisKey(new CacheKey(message.Key));
    }

    private async Task IndexTagsAsync(RedisKey redisKey, IEnumerable<string> tags, TimeSpan? expiry)
    {
        var normalized = tags?.Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (normalized.Length == 0)
        {
            return;
        }

        foreach (var tag in normalized)
        {
            var tagKey = BuildTagKey(tag);
            var keyString = redisKey.ToString();
            if (string.IsNullOrEmpty(keyString))
            {
                continue;
            }

            var namespacedKey = (RedisValue)keyString!;
            await _database.SetAddAsync(tagKey, namespacedKey);
            if (expiry.HasValue)
            {
                await _database.KeyExpireAsync(tagKey, expiry);
            }
        }
    }

    private async Task RemoveTagsAsync(RedisKey redisKey, IEnumerable<string> tags)
    {
        if (tags is null)
        {
            return;
        }

        var keyString = redisKey.ToString();
        if (string.IsNullOrEmpty(keyString))
        {
            return;
        }

        var namespacedKey = (RedisValue)keyString!;
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var tagKey = BuildTagKey(tag.Trim());
            await _database.SetRemoveAsync(tagKey, namespacedKey);
        }
    }

    private async Task EvictAsync(RedisKey redisKey, IEnumerable<string> tags)
    {
        await _database.KeyDeleteAsync(redisKey);
        await RemoveTagsAsync(redisKey, tags);
    }

    private static TimeSpan? DetermineExpiry(DateTimeOffset now, DateTimeOffset? absoluteExpiration, DateTimeOffset? staleUntil)
    {
        if (staleUntil.HasValue)
        {
            var ttl = staleUntil.Value - now;
            if (ttl > TimeSpan.Zero)
            {
                return ttl;
            }

            return TimeSpan.Zero;
        }

        if (absoluteExpiration.HasValue)
        {
            var ttl = absoluteExpiration.Value - now;
            if (ttl > TimeSpan.Zero)
            {
                return ttl;
            }

            return TimeSpan.Zero;
        }

        return null;
    }

    private RedisKey BuildRedisKey(CacheKey key) => (RedisKey)($"{_keyPrefix}{_instancePrefix}{key.Value}");

    private RedisKey BuildTagKey(string tag) => (RedisKey)($"{_tagPrefix}{_instancePrefix}{tag}");

    private static string NormalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        while (trimmed.EndsWith(":", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Length == 0 ? string.Empty : trimmed + ":";
    }
}
