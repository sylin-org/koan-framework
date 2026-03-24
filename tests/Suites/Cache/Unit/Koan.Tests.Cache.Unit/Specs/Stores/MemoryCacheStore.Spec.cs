using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Adapter.Memory.Options;
using Koan.Cache.Adapter.Memory.Stores;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Koan.Tests.Cache.Unit.Specs.Stores;

public sealed class MemoryCacheStoreSpec
{
    private readonly ITestOutputHelper _output;

    public MemoryCacheStoreSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task FetchAsync_hit_before_expiration_returns_value()
        => Spec(nameof(FetchAsync_hit_before_expiration_returns_value), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache);
            var key = new CacheKey("memory-hit");
            var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromSeconds(1) };

            await store.Set(key, CacheValue.FromString("value"), options, CancellationToken.None);
            var result = await store.Fetch(key, options, CancellationToken.None);

            result.Hit.Should().BeTrue();
            result.Value!.ToText().Should().Be("value");
        });

    [Fact]
    public Task FetchAsync_returns_miss_after_expiration_when_stale_disabled()
        => Spec(nameof(FetchAsync_returns_miss_after_expiration_when_stale_disabled), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache);
            var key = new CacheKey("memory-expire");
            var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromMilliseconds(75) };

            await store.Set(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await Task.Delay(150);

            var result = await store.Fetch(key, options, CancellationToken.None);
            result.Hit.Should().BeFalse();
        });

    [Fact]
    public Task FetchAsync_serves_stale_when_enabled()
        => Spec(nameof(FetchAsync_serves_stale_when_enabled), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache, enableStale: true);
            var key = new CacheKey("memory-stale");
            var options = new CacheEntryOptions
            {
                AbsoluteTtl = TimeSpan.FromMilliseconds(50),
                AllowStaleFor = TimeSpan.FromMilliseconds(200)
            };

            await store.Set(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await Task.Delay(120);

            var result = await store.Fetch(key, options, CancellationToken.None);
            result.Hit.Should().BeTrue();
            result.Value!.ToText().Should().Be("value");
        });

    [Fact]
    public Task ExistsAsync_returns_false_after_expiration_when_stale_disabled()
        => Spec(nameof(ExistsAsync_returns_false_after_expiration_when_stale_disabled), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache);
            var key = new CacheKey("memory-exists-expired");
            var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromMilliseconds(50) };

            await store.Set(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await Task.Delay(90);

            var exists = await store.Exists(key, CancellationToken.None);
            exists.Should().BeFalse();
        });

    [Fact]
    public Task EnumerateByTagAsync_returns_tagged_entries()
        => Spec(nameof(EnumerateByTagAsync_returns_tagged_entries), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache, enableStale: true);
            var key = new CacheKey("memory-tag");
            var options = new CacheEntryOptions().WithTags("alpha");

            await store.Set(key, CacheValue.FromString("value"), options, CancellationToken.None);

            var items = new List<TaggedCacheKey>();
            await foreach (var item in store.EnumerateByTag("alpha", CancellationToken.None))
            {
                items.Add(item);
            }

            items.Should().ContainSingle(i => i.Key.Matches(key.Value));
        });

    [Fact]
    public Task RemoveAsync_prunes_tag_index()
        => Spec(nameof(RemoveAsync_prunes_tag_index), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache, enableStale: true);
            var key = new CacheKey("memory-remove");
            var options = new CacheEntryOptions().WithTags("beta");

            await store.Set(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await store.Remove(key, CancellationToken.None);

            var results = new List<TaggedCacheKey>();
            await foreach (var item in store.EnumerateByTag("beta", CancellationToken.None))
            {
                results.Add(item);
            }

            results.Should().BeEmpty();
        });

    private static MemoryCacheStore CreateStore(IMemoryCache cache, bool enableStale = false, int tagCapacity = 16)
    {
    var options = global::Microsoft.Extensions.Options.Options.Create(new MemoryCacheAdapterOptions
        {
            EnableStaleWhileRevalidate = enableStale,
            TagIndexCapacity = tagCapacity
        });

        return new MemoryCacheStore(cache, options, NullLogger<MemoryCacheStore>.Instance);
    }

    private Task Spec(string scenario, Func<Task> body)
        => TestPipeline.For<MemoryCacheStoreSpec>(_output, scenario)
            .Assert(async _ =>
            {
                await body().ConfigureAwait(false);
            })
            .Run();
}
