using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Media.Web.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Tests.Cache.Web.Specs;

/// <summary>
/// M11 pilot migration verification: the transform cache is now pillar-backed.
/// Validates the round-trip + the per-entry size cap + the best-effort write contract
/// using a hand-rolled <c>ICacheClient</c> double — no need to spin up a real layered
/// cache to verify the contract surface.
/// </summary>
public sealed class MediaTransformCacheSpec
{
    private static IOptions<MediaTransformCacheOptions> Options(MediaTransformCacheOptions opts)
        => Microsoft.Extensions.Options.Options.Create(opts);

    private static MediaTransformCache BuildCache(ICacheClient client, MediaTransformCacheOptions? options = null)
        => new(client, Options(options ?? new MediaTransformCacheOptions()), NullLogger<MediaTransformCache>.Instance);

    [Fact]
    public async Task WriteAsync_then_TryGetAsync_round_trips_the_entry()
    {
        var fake = new FakeCacheClient();
        var cache = BuildCache(fake);
        var entry = new MediaCacheEntry(new byte[] { 1, 2, 3, 4 }, "image/webp", "\"abc\"");

        await cache.WriteAsync("photo:thumb-256", entry, CancellationToken.None);
        var roundTripped = await cache.TryGetAsync("photo:thumb-256", CancellationToken.None);

        roundTripped.Should().NotBeNull();
        roundTripped!.Bytes.Should().Equal(entry.Bytes);
        roundTripped.ContentType.Should().Be("image/webp");
        roundTripped.ETag.Should().Be("\"abc\"");
    }

    [Fact]
    public async Task TryGetAsync_returns_null_on_miss()
    {
        var cache = BuildCache(new FakeCacheClient());

        var result = await cache.TryGetAsync("nothing-here", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_skips_entry_larger_than_MaxEntryBytes()
    {
        var fake = new FakeCacheClient();
        var cache = BuildCache(fake, new MediaTransformCacheOptions { MaxEntryBytes = 1024 });

        var oversize = new MediaCacheEntry(new byte[2048], "image/jpeg", "\"big\"");
        await cache.WriteAsync("photo:huge", oversize, CancellationToken.None);

        fake.SetCount.Should().Be(0, "entries larger than MaxEntryBytes must not be written");
    }

    [Fact]
    public async Task Key_carries_configured_prefix()
    {
        var fake = new FakeCacheClient();
        var cache = BuildCache(fake, new MediaTransformCacheOptions { KeyPrefix = "mt:" });
        var entry = new MediaCacheEntry(new byte[] { 9 }, "image/png", "\"x\"");

        await cache.WriteAsync("foo", entry, CancellationToken.None);

        fake.LastWrittenKey.Should().Be("mt:foo");
    }

    [Fact]
    public async Task WriteAsync_applies_AbsoluteExpiration_via_builder()
    {
        var fake = new FakeCacheClient();
        var ttl = TimeSpan.FromHours(2);
        var cache = BuildCache(fake, new MediaTransformCacheOptions { AbsoluteExpiration = ttl });
        var entry = new MediaCacheEntry(new byte[] { 1 }, "image/png", "\"t\"");

        await cache.WriteAsync("ttl-test", entry, CancellationToken.None);

        fake.LastWriteOptions.Should().NotBeNull();
        fake.LastWriteOptions!.AbsoluteTtl.Should().Be(ttl);
    }

    [Fact]
    public async Task WriteAsync_tags_entry_with_media_transform_tag_by_default()
    {
        var fake = new FakeCacheClient();
        var cache = BuildCache(fake);
        var entry = new MediaCacheEntry(new byte[] { 1 }, "image/png", "\"t\"");

        await cache.WriteAsync("tagged", entry, CancellationToken.None);

        fake.LastWriteOptions!.Tags.Should().Contain("media-transform",
            "default tag enables bulk-flush via Cache.Tags(\"media-transform\").Flush()");
    }

    [Fact]
    public async Task WriteAsync_suppresses_coherence_broadcast_for_populates()
    {
        var fake = new FakeCacheClient();
        var cache = BuildCache(fake);
        var entry = new MediaCacheEntry(new byte[] { 1 }, "image/png", "\"t\"");

        await cache.WriteAsync("no-broadcast", entry, CancellationToken.None);

        fake.LastWriteOptions!.ForceCoherenceBroadcast.Should().BeFalse(
            "transform writes are cache populates, not data changes — peers should not be evicted");
    }

    [Fact]
    public async Task WriteAsync_swallows_exceptions_to_honour_best_effort_contract()
    {
        var cache = BuildCache(new ThrowingCacheClient());
        var entry = new MediaCacheEntry(new byte[] { 1 }, "image/png", "\"t\"");

        var act = async () => await cache.WriteAsync("explodes", entry, CancellationToken.None);

        await act.Should().NotThrowAsync(
            "the controller already streamed bytes to the client — cache failure must not propagate");
    }

    [Fact]
    public async Task TryGetAsync_returns_null_when_underlying_cache_throws()
    {
        var cache = BuildCache(new ThrowingCacheClient());

        var result = await cache.TryGetAsync("explodes-on-read", CancellationToken.None);

        result.Should().BeNull("flaky cache reads must fall through to recompute, not propagate");
    }
}

/// <summary>Minimal <see cref="ICacheClient"/> double that captures the most-recent write for assertion.</summary>
internal sealed class FakeCacheClient : ICacheClient
{
    private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

    public int SetCount { get; private set; }
    public string? LastWrittenKey { get; private set; }
    public CacheEntryOptions? LastWriteOptions { get; private set; }

    public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key)
        => new FakeBuilder<T>(this, key);

    public CacheScopeHandle BeginScope(string scopeId, string? region = null)
        => new(scopeId, region, () => { });

    public ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, CancellationToken ct) => ValueTask.FromResult(0L);
    public ValueTask<long> CountTags(IReadOnlyCollection<string> tags, CancellationToken ct) => ValueTask.FromResult(0L);

    public ValueTask<CacheFetchResult> Get(CacheKey key, CacheEntryOptions options, CancellationToken ct) => default;
    public ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct) => default;
    public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct) => default;
    public ValueTask<bool> Exists(CacheKey key, CacheEntryOptions options, CancellationToken ct) => ValueTask.FromResult(_store.ContainsKey(key.Value));
    public ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct) { _store[key.Value] = value; return ValueTask.CompletedTask; }
    public ValueTask<bool> Remove(CacheKey key, CancellationToken ct) => ValueTask.FromResult(_store.Remove(key.Value));
    public ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct) => ValueTask.CompletedTask;

    internal void Record(string key, CacheEntryOptions options, object? value)
    {
        SetCount++;
        LastWrittenKey = key;
        LastWriteOptions = options;
        _store[key] = value;
    }

    internal bool TryGet(string key, out object? value) => _store.TryGetValue(key, out value);

    private sealed class FakeBuilder<T> : ICacheEntryBuilder<T>
    {
        private readonly FakeCacheClient _client;
        private CacheEntryOptions _options = new();

        public FakeBuilder(FakeCacheClient client, CacheKey key) { _client = client; Key = key; }

        public CacheKey Key { get; }
        public CacheEntryOptions Options => _options;

        public ICacheEntryBuilder<T> WithOptions(Func<CacheEntryOptions, CacheEntryOptions> configure) { _options = configure(_options); return this; }
        public ICacheEntryBuilder<T> WithAbsoluteTtl(TimeSpan ttl) { _options = _options with { AbsoluteTtl = ttl }; return this; }
        public ICacheEntryBuilder<T> WithSlidingTtl(TimeSpan ttl) { _options = _options with { SlidingTtl = ttl }; return this; }
        public ICacheEntryBuilder<T> AllowStaleFor(TimeSpan duration) { _options = _options with { AllowStaleFor = duration }; return this; }
        public ICacheEntryBuilder<T> WithTags(params string[] tags) { _options = _options.WithTags(tags); return this; }
        public ICacheEntryBuilder<T> WithContentKind(CacheContentKind kind) { _options = _options with { ContentKind = kind }; return this; }
        public ICacheEntryBuilder<T> BroadcastInvalidation(bool value = true) { _options = _options with { ForceCoherenceBroadcast = value }; return this; }
        public ICacheEntryBuilder<T> WithConsistency(CacheConsistencyMode mode) { _options = _options with { Consistency = mode }; return this; }

        public ValueTask<T?> Get(CancellationToken ct)
            => _client.TryGet(Key.Value, out var v) && v is T t
                ? ValueTask.FromResult<T?>(t)
                : ValueTask.FromResult<T?>(default);
        public ValueTask<T?> GetOrAdd(Func<CancellationToken, ValueTask<T?>> valueFactory, CancellationToken ct) => Get(ct);
        public ValueTask Set(T value, CancellationToken ct) { _client.Record(Key.Value, _options, value); return ValueTask.CompletedTask; }
        public ValueTask Remove(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask Touch(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<bool> Exists(CancellationToken ct) => ValueTask.FromResult(_client.TryGet(Key.Value, out _));
    }
}

internal sealed class ThrowingCacheClient : ICacheClient
{
    public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key) => new ThrowingBuilder<T>(key);
    public CacheScopeHandle BeginScope(string scopeId, string? region = null) => new(scopeId, region, () => { });
    public ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, CancellationToken ct) => ValueTask.FromResult(0L);
    public ValueTask<long> CountTags(IReadOnlyCollection<string> tags, CancellationToken ct) => ValueTask.FromResult(0L);
    public ValueTask<CacheFetchResult> Get(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new InvalidOperationException("read down");
    public ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new InvalidOperationException("read down");
    public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct) => throw new InvalidOperationException("read down");
    public ValueTask<bool> Exists(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new InvalidOperationException("read down");
    public ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct) => throw new InvalidOperationException("write down");
    public ValueTask<bool> Remove(CacheKey key, CancellationToken ct) => throw new InvalidOperationException("write down");
    public ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new InvalidOperationException("write down");

    private sealed class ThrowingBuilder<T> : ICacheEntryBuilder<T>
    {
        public ThrowingBuilder(CacheKey key) { Key = key; }
        public CacheKey Key { get; }
        public CacheEntryOptions Options => new();
        public ICacheEntryBuilder<T> WithOptions(Func<CacheEntryOptions, CacheEntryOptions> configure) => this;
        public ICacheEntryBuilder<T> WithAbsoluteTtl(TimeSpan ttl) => this;
        public ICacheEntryBuilder<T> WithSlidingTtl(TimeSpan ttl) => this;
        public ICacheEntryBuilder<T> AllowStaleFor(TimeSpan duration) => this;
        public ICacheEntryBuilder<T> WithTags(params string[] tags) => this;
        public ICacheEntryBuilder<T> WithContentKind(CacheContentKind kind) => this;
        public ICacheEntryBuilder<T> BroadcastInvalidation(bool value = true) => this;
        public ICacheEntryBuilder<T> WithConsistency(CacheConsistencyMode mode) => this;
        public ValueTask<T?> Get(CancellationToken ct) => throw new InvalidOperationException("read down");
        public ValueTask<T?> GetOrAdd(Func<CancellationToken, ValueTask<T?>> valueFactory, CancellationToken ct) => throw new InvalidOperationException("read down");
        public ValueTask Set(T value, CancellationToken ct) => throw new InvalidOperationException("write down");
        public ValueTask Remove(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask Touch(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<bool> Exists(CancellationToken ct) => throw new InvalidOperationException("read down");
    }
}
