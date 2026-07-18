using System;
using System.Threading.Tasks;
using Koan.Cache.Adapter.Redis.Tests.Support;

namespace Koan.Cache.Adapter.Redis.Tests.Specs;

/// <summary>
/// Integration tests for the Redis L2 store. Proves writes survive across separate
/// <c>ICacheClient</c> instances (sharing the same Redis container), tag indexing roundtrips,
/// and tag-flush actually removes keys from L2.
/// </summary>
public sealed class RedisCacheAdapterSpec(RedisFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task Set_and_fetch_roundtrip_across_clients()
    {
        Assert.SkipWhen(!fixture.IsAvailable, fixture.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.CreateVersion7().ToString("N");
        var keyPrefix = $"cache:{token}:";
        var tagPrefix = $"cache:tag:{token}:";
        var channel = $"koan-cache-{token}";

        await using var nodeA = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ct);
        await using var nodeB = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ct);

        var clientA = nodeA.Provider.GetRequiredService<ICacheClient>();
        var clientB = nodeB.Provider.GetRequiredService<ICacheClient>();

        var key = new CacheKey($"redis-roundtrip-{token}");
        await clientA.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
            .WithTags("redis-integration", token)
            .Set("payload", ct);

        // B reads from a separate node — must hit L2 (B has no L1 entry for this key).
        var read = await clientB.CreateEntry<string>(key).Get(ct);
        read.Should().Be("payload");

        // Tag index roundtrip via L2.
        var tagCount = await clientB.CountTags(new[] { "redis-integration" }, ct);
        tagCount.Should().Be(1);

        // Tag flush removes from L2 + broadcasts EvictByTag → both nodes evict L1.
        var removed = await clientB.FlushTags(new[] { "redis-integration" }, ct);
        removed.Should().Be(1);

        // After flush, both nodes return null.
        var afterFlushB = await clientB.CreateEntry<string>(key).Get(ct);
        afterFlushB.Should().BeNull();

        // Give the broadcast a moment to reach A; then A is also empty.
        await Task.Delay(250, ct);
        var afterFlushA = await clientA.CreateEntry<string>(key).Get(ct);
        afterFlushA.Should().BeNull();
    }

    [Fact]
    public async Task Opt_in_AllowStaleFor_surfaces_stale_values_within_window()
    {
        Assert.SkipWhen(!fixture.IsAvailable, fixture.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.CreateVersion7().ToString("N");
        var keyPrefix = $"cache:{token}:";
        var tagPrefix = $"cache:tag:{token}:";
        var channel = $"koan-cache-{token}-stale";

        await using var node = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ct);
        var client = node.Provider.GetRequiredService<ICacheClient>();

        // The caller explicitly opts into bounded stale serving via .AllowStaleFor(...). The same
        // option is used for the write (records staleUntil at write time) and the read
        // (signals "I'll accept stale within this window").
        var key = new CacheKey($"redis-swr-optin-{token}");
        var entry = client.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMilliseconds(200))
            .AllowStaleFor(TimeSpan.FromSeconds(2));

        await entry.Set("payload", ct);

        // Past absolute TTL but within the bounded stale window → opted-in caller gets stale value.
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        var stale = await entry.Get(ct);
        stale.Should().Be("payload");

        // Past the bounded stale allowance → null even for opted-in caller.
        await Task.Delay(TimeSpan.FromMilliseconds(2500), ct);
        var final = await entry.Get(ct);
        final.Should().BeNull();
    }

    [Fact]
    public async Task Default_strict_consistency_returns_null_past_absolute_TTL()
    {
        Assert.SkipWhen(!fixture.IsAvailable, fixture.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.CreateVersion7().ToString("N");
        var keyPrefix = $"cache:{token}:";
        var tagPrefix = $"cache:tag:{token}:";
        var channel = $"koan-cache-{token}-strict";

        await using var node = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ct);
        var client = node.Provider.GetRequiredService<ICacheClient>();

        var key = new CacheKey($"redis-strict-{token}");

        // No AllowStaleFor — default consistency is Strict. Even if the entry could have
        // been preserved (no staleness window), past the absolute TTL the read returns null.
        var entry = client.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMilliseconds(200));

        await entry.Set("payload", ct);

        // Within TTL → hit.
        var fresh = await entry.Get(ct);
        fresh.Should().Be("payload");

        // Past TTL → null (no opt-in, default strict).
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        var after = await entry.Get(ct);
        after.Should().BeNull("fresh-or-miss is the default; stale serving requires explicit AllowStaleFor");
    }
}
