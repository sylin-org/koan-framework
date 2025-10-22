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
        => SpecAsync(nameof(FetchAsync_hit_before_expiration_returns_value), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache);
            var key = new CacheKey("memory-hit");
            var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromSeconds(1) };

            await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
            var result = await store.FetchAsync(key, options, CancellationToken.None);

            result.Hit.Should().BeTrue();
            result.Value!.ToText().Should().Be("value");
        });

    [Fact]
    public Task FetchAsync_returns_miss_after_expiration_when_stale_disabled()
        => SpecAsync(nameof(FetchAsync_returns_miss_after_expiration_when_stale_disabled), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache);
            var key = new CacheKey("memory-expire");
            var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromMilliseconds(75) };

            await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await Task.Delay(150);

            var result = await store.FetchAsync(key, options, CancellationToken.None);
            result.Hit.Should().BeFalse();
        });

    [Fact]
    public Task FetchAsync_serves_stale_when_enabled()
        => SpecAsync(nameof(FetchAsync_serves_stale_when_enabled), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache, enableStale: true);
            var key = new CacheKey("memory-stale");
            var options = new CacheEntryOptions
            {
                AbsoluteTtl = TimeSpan.FromMilliseconds(50),
                AllowStaleFor = TimeSpan.FromMilliseconds(200)
            };

            await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await Task.Delay(120);

            var result = await store.FetchAsync(key, options, CancellationToken.None);
            result.Hit.Should().BeTrue();
            result.Value!.ToText().Should().Be("value");
        });

    [Fact]
    public Task ExistsAsync_returns_false_after_expiration_when_stale_disabled()
        => SpecAsync(nameof(ExistsAsync_returns_false_after_expiration_when_stale_disabled), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache);
            var key = new CacheKey("memory-exists-expired");
            var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromMilliseconds(50) };

            await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await Task.Delay(90);

            var exists = await store.ExistsAsync(key, CancellationToken.None);
            exists.Should().BeFalse();
        });

    [Fact]
    public Task EnumerateByTagAsync_returns_tagged_entries()
        => SpecAsync(nameof(EnumerateByTagAsync_returns_tagged_entries), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache, enableStale: true);
            var key = new CacheKey("memory-tag");
            var options = new CacheEntryOptions().WithTags("alpha");

            await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);

            var items = new List<TaggedCacheKey>();
            await foreach (var item in store.EnumerateByTagAsync("alpha", CancellationToken.None))
            {
                items.Add(item);
            }

            items.Should().ContainSingle(i => i.Key.Matches(key.Value));
        });

    [Fact]
    public Task RemoveAsync_prunes_tag_index()
        => SpecAsync(nameof(RemoveAsync_prunes_tag_index), async () =>
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var store = CreateStore(cache, enableStale: true);
            var key = new CacheKey("memory-remove");
            var options = new CacheEntryOptions().WithTags("beta");

            await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
            await store.RemoveAsync(key, CancellationToken.None);

            var results = new List<TaggedCacheKey>();
            await foreach (var item in store.EnumerateByTagAsync("beta", CancellationToken.None))
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

    private Task SpecAsync(string scenario, Func<Task> body)
        => TestPipeline.For<MemoryCacheStoreSpec>(_output, scenario)
            .Assert(async _ =>
            {
                await body().ConfigureAwait(false);
            })
            .RunAsync();
}
