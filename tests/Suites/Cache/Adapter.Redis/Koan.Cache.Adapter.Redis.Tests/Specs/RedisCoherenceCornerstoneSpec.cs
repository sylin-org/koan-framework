using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Adapter.Redis.Tests.Support;

namespace Koan.Cache.Adapter.Redis.Tests.Specs;

/// <summary>
/// The M4 cornerstone integration test: two Koan cache nodes share one Redis container and
/// one pub/sub channel. Proves the asymmetric coherence model end-to-end with real wire
/// transport — writes on node A broadcast EvictKey via Redis pub/sub, node B's L1 evicts,
/// and node B's next read returns the fresh value from L2.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this test matters:</b> every unit spec in <c>Koan.Tests.Cache.*</c> uses an
/// in-process fake coherence bus. This is the only spec that proves the Redis adapter's
/// serialization, channel naming, and pub/sub delivery actually work against a real Redis
/// daemon. If this passes, the multi-instance story is real.
/// </para>
/// <para>
/// <b>Asymmetric model recap</b> (per ARCH-0075):
/// <list type="bullet">
///   <item>Writer (A) write-through to L1 + L2, then broadcasts EvictKey.</item>
///   <item>Peer (B) receives → evicts its L1 (L1 only — never L2, never re-broadcast).</item>
///   <item>Peer's next read goes L1 (miss) → L2 (hit, fresh) → populates L1.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RedisCoherenceCornerstoneSpec(RedisFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task Write_on_A_invalidates_L1_on_B_via_redis_pubsub()
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

        var key = new CacheKey($"coherence-{token}");

        // 1. A writes v1 (write-through L1+L2, broadcasts EvictKey).
        await clientA.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
            .Set("v1", ct);

        // 2. B reads → L1 miss → L2 hit → populates B's L1 with "v1".
        var read1 = await clientB.CreateEntry<string>(key).Get(ct);
        read1.Should().Be("v1");

        // 3. A overwrites with v2 — broadcasts EvictKey via real Redis pub/sub.
        await clientA.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
            .Set("v2", ct);

        // 4. Poll B's read until it sees v2 (eventually consistent within seconds).
        //    Reading too fast would still return v1 from B's L1 because the
        //    coordinator's OnReceived handler runs asynchronously after publish.
        var read2 = await PollUntil(
            () => clientB.CreateEntry<string>(key).Get(ct),
            value => value == "v2",
            TimeSpan.FromSeconds(5),
            ct);

        read2.Should().Be("v2");
    }

    [Fact]
    public async Task Tag_flush_on_A_invalidates_tagged_keys_on_B()
    {
        Assert.SkipWhen(!fixture.IsAvailable, fixture.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;

        var token = Guid.CreateVersion7().ToString("N");
        var keyPrefix = $"cache:{token}:";
        var tagPrefix = $"cache:tag:{token}:";
        var channel = $"koan-cache-{token}-tags";
        var tag = $"product:{token}";

        await using var nodeA = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ct);
        await using var nodeB = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ct);

        var clientA = nodeA.Provider.GetRequiredService<ICacheClient>();
        var clientB = nodeB.Provider.GetRequiredService<ICacheClient>();

        var key1 = new CacheKey($"prod-1-{token}");
        var key2 = new CacheKey($"prod-2-{token}");

        // A writes two tagged entries.
        await clientA.CreateEntry<string>(key1).WithTags(tag).Set("alpha", ct);
        await clientA.CreateEntry<string>(key2).WithTags(tag).Set("beta", ct);

        // B populates its L1 by reading both.
        (await clientB.CreateEntry<string>(key1).Get(ct)).Should().Be("alpha");
        (await clientB.CreateEntry<string>(key2).Get(ct)).Should().Be("beta");

        // A flushes the tag — broadcasts EvictByTag via Redis pub/sub.
        var removed = await clientA.FlushTags(new[] { tag }, ct);
        removed.Should().Be(2);

        // Poll B's reads until both keys return null (broadcast propagated, L1 evicted,
        // L2 already flushed by A → both layers empty).
        var read1AfterFlush = await PollUntil(
            () => clientB.CreateEntry<string>(key1).Get(ct),
            value => value is null,
            TimeSpan.FromSeconds(5),
            ct);
        read1AfterFlush.Should().BeNull();

        (await clientB.CreateEntry<string>(key2).Get(ct)).Should().BeNull();
    }

    /// <summary>
    /// Poll an async value-producer until the predicate is satisfied or the deadline expires.
    /// Used to wait for eventually-consistent state (coherence broadcasts) without flat
    /// <c>Task.Delay</c> guesses that flake on slow CI.
    /// </summary>
    private static async Task<T> PollUntil<T>(
        Func<ValueTask<T>> produce,
        Func<T, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        T last = default!;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await produce().ConfigureAwait(false);
            if (predicate(last)) return last;
            await Task.Delay(25, ct).ConfigureAwait(false);
        }

        return last;
    }
}
