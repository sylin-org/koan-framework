using System;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Adapter.Redis.Tests.Support;
using Koan.Testing.Extensions;
using Koan.Testing.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Cache.Adapter.Redis.Tests.Specs;

/// <summary>
/// Integration tests for the Redis L2 store. Proves writes survive across separate
/// <c>ICacheClient</c> instances (sharing the same Redis container), tag indexing roundtrips,
/// and tag-flush actually removes keys from L2.
/// </summary>
public sealed class RedisCacheAdapterSpec
{
    private readonly ITestOutputHelper _output;

    public RedisCacheAdapterSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Set_and_fetch_roundtrip_across_clients()
        => TestPipeline.For<RedisCacheAdapterSpec>(_output, nameof(Set_and_fetch_roundtrip_across_clients))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var fixture = ctx.GetRedisFixture();
                if (!fixture.IsAvailable || string.IsNullOrWhiteSpace(fixture.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {fixture.UnavailableReason ?? "unspecified"}");

                var token = ctx.ExecutionId.ToString("N");
                var keyPrefix = $"cache:{token}:";
                var tagPrefix = $"cache:tag:{token}:";
                var channel = $"koan-cache-{token}";

                await using var nodeA = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ctx.Cancellation);
                await using var nodeB = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ctx.Cancellation);

                var clientA = nodeA.Provider.GetRequiredService<ICacheClient>();
                var clientB = nodeB.Provider.GetRequiredService<ICacheClient>();

                var key = new CacheKey($"redis-roundtrip-{token}");
                await clientA.CreateEntry<string>(key)
                    .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
                    .WithTags("redis-integration", token)
                    .Set("payload", ctx.Cancellation);

                // B reads from a separate node — must hit L2 (B has no L1 entry for this key).
                var read = await clientB.CreateEntry<string>(key).Get(ctx.Cancellation);
                read.Should().Be("payload");

                // Tag index roundtrip via L2.
                var tagCount = await clientB.CountTags(new[] { "redis-integration" }, ctx.Cancellation);
                tagCount.Should().Be(1);

                // Tag flush removes from L2 + broadcasts EvictByTag → both nodes evict L1.
                var removed = await clientB.FlushTags(new[] { "redis-integration" }, ctx.Cancellation);
                removed.Should().Be(1);

                // After flush, both nodes return null.
                var afterFlushB = await clientB.CreateEntry<string>(key).Get(ctx.Cancellation);
                afterFlushB.Should().BeNull();

                // Give the broadcast a moment to reach A; then A is also empty.
                await Task.Delay(250, ctx.Cancellation);
                var afterFlushA = await clientA.CreateEntry<string>(key).Get(ctx.Cancellation);
                afterFlushA.Should().BeNull();
            })
            .Run();

    [Fact]
    public Task Opt_in_AllowStaleFor_surfaces_stale_values_within_window()
        => TestPipeline.For<RedisCacheAdapterSpec>(_output, nameof(Opt_in_AllowStaleFor_surfaces_stale_values_within_window))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var fixture = ctx.GetRedisFixture();
                if (!fixture.IsAvailable || string.IsNullOrWhiteSpace(fixture.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {fixture.UnavailableReason ?? "unspecified"}");

                var token = ctx.ExecutionId.ToString("N");
                var keyPrefix = $"cache:{token}:";
                var tagPrefix = $"cache:tag:{token}:";
                var channel = $"koan-cache-{token}-stale";

                await using var node = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ctx.Cancellation);
                var client = node.Provider.GetRequiredService<ICacheClient>();

                // Per ARCH-0078: caller explicitly opts into SWR via .AllowStaleFor(...). The same
                // option is used for the write (records staleUntil at write time) and the read
                // (signals "I'll accept stale within this window").
                var key = new CacheKey($"redis-swr-optin-{token}");
                var entry = client.CreateEntry<string>(key)
                    .WithAbsoluteTtl(TimeSpan.FromMilliseconds(200))
                    .AllowStaleFor(TimeSpan.FromSeconds(2));

                await entry.Set("payload", ctx.Cancellation);

                // Past absolute TTL but within SWR window → opted-in caller gets stale value.
                await Task.Delay(TimeSpan.FromMilliseconds(300), ctx.Cancellation);
                var stale = await entry.Get(ctx.Cancellation);
                stale.Should().Be("payload");

                // Past SWR allowance → null even for opted-in caller.
                await Task.Delay(TimeSpan.FromMilliseconds(2500), ctx.Cancellation);
                var final = await entry.Get(ctx.Cancellation);
                final.Should().BeNull();
            })
            .Run();

    [Fact]
    public Task Default_strict_consistency_returns_null_past_absolute_TTL()
        => TestPipeline.For<RedisCacheAdapterSpec>(_output, nameof(Default_strict_consistency_returns_null_past_absolute_TTL))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var fixture = ctx.GetRedisFixture();
                if (!fixture.IsAvailable || string.IsNullOrWhiteSpace(fixture.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {fixture.UnavailableReason ?? "unspecified"}");

                var token = ctx.ExecutionId.ToString("N");
                var keyPrefix = $"cache:{token}:";
                var tagPrefix = $"cache:tag:{token}:";
                var channel = $"koan-cache-{token}-strict";

                await using var node = await RedisCacheNode.Start(fixture.ConnectionString!, keyPrefix, tagPrefix, channel, ctx.Cancellation);
                var client = node.Provider.GetRequiredService<ICacheClient>();

                var key = new CacheKey($"redis-strict-{token}");

                // No AllowStaleFor — default consistency is Strict. Even if the entry could have
                // been preserved (no staleness window), past the absolute TTL the read returns null.
                var entry = client.CreateEntry<string>(key)
                    .WithAbsoluteTtl(TimeSpan.FromMilliseconds(200));

                await entry.Set("payload", ctx.Cancellation);

                // Within TTL → hit.
                var fresh = await entry.Get(ctx.Cancellation);
                fresh.Should().Be("payload");

                // Past TTL → null (no opt-in, default strict).
                await Task.Delay(TimeSpan.FromMilliseconds(300), ctx.Cancellation);
                var after = await entry.Get(ctx.Cancellation);
                after.Should().BeNull("default consistency is strict — no SWR without explicit AllowStaleFor (ARCH-0078)");
            })
            .Run();
}
