using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Adapter.Memory.Options;
using Koan.Cache.Adapter.Memory.Stores;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Cache.Tests;

public sealed class MemoryCacheStoreTests
{
    [Fact]
    public async Task FetchAsync_ReturnsHitBeforeExpiration()
    {
        var store = CreateStore(enableStale: false);
        var key = new CacheKey("memory-hit");
        var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromSeconds(1) };

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
        var result = await store.FetchAsync(key, options, CancellationToken.None);

        result.Hit.Should().BeTrue();
        result.Value!.ToText().Should().Be("value");
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueBeforeExpiration()
    {
        var store = CreateStore(enableStale: false);
        var key = new CacheKey("memory-exists-live");
        var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromSeconds(1) };

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);

        var exists = await store.ExistsAsync(key, CancellationToken.None);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_ReturnsMissAfterExpirationWhenStaleDisabled()
    {
        var store = CreateStore(enableStale: false);
        var key = new CacheKey("memory-expire");
        var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromMilliseconds(100) };

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
        await Task.Delay(150);

        var result = await store.FetchAsync(key, options, CancellationToken.None);
        result.Hit.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_ServesStaleWhenEnabled()
    {
        var store = CreateStore(enableStale: true);
        var key = new CacheKey("memory-stale");
        var options = new CacheEntryOptions
        {
            AbsoluteTtl = TimeSpan.FromMilliseconds(50),
            AllowStaleFor = TimeSpan.FromMilliseconds(200)
        };

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
        await Task.Delay(120);

        var result = await store.FetchAsync(key, options, CancellationToken.None);
        result.Hit.Should().BeTrue("stale-while-revalidate keeps serving stale value");
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseAfterExpiryWhenStaleDisabled()
    {
        var store = CreateStore(enableStale: false);
        var key = new CacheKey("memory-exists-expired");
        var options = new CacheEntryOptions { AbsoluteTtl = TimeSpan.FromMilliseconds(50) };

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
        await Task.Delay(75);

        var exists = await store.ExistsAsync(key, CancellationToken.None);

        exists.Should().BeFalse();
        var result = await store.FetchAsync(key, new CacheEntryOptions(), CancellationToken.None);
        result.Hit.Should().BeFalse();
    }

    [Fact]
    public async Task EnumerateByTagAsync_ReturnsTaggedEntries()
    {
        var store = CreateStore(enableStale: true);
        var key = new CacheKey("memory-tag");
        var options = new CacheEntryOptions().WithTags("alpha");

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);

        var results = new List<TaggedCacheKey>();
        await foreach (var item in store.EnumerateByTagAsync("alpha", CancellationToken.None))
        {
            results.Add(item);
        }

        results.Should().ContainSingle(r => r.Key.Matches(key.Value));
    }

    [Fact]
    public async Task RemoveAsync_PrunesTagIndex()
    {
        var store = CreateStore(enableStale: true);
        var key = new CacheKey("memory-remove");
        var options = new CacheEntryOptions().WithTags("beta");

        await store.SetAsync(key, CacheValue.FromString("value"), options, CancellationToken.None);
        await store.RemoveAsync(key, CancellationToken.None);

        var items = new List<TaggedCacheKey>();
        await foreach (var item in store.EnumerateByTagAsync("beta", CancellationToken.None))
        {
            items.Add(item);
        }

        items.Should().BeEmpty();
    }

    private static MemoryCacheStore CreateStore(bool enableStale)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Microsoft.Extensions.Options.Options.Create(new MemoryCacheAdapterOptions
        {
            EnableStaleWhileRevalidate = enableStale,
            TagIndexCapacity = 16
        });

        return new MemoryCacheStore(cache, options, NullLogger<MemoryCacheStore>.Instance);
    }
}
