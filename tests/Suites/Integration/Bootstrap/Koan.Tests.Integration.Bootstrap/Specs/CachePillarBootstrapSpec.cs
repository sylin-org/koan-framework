using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core;
using Koan.Testing.Extensions;
using Koan.Testing.Integration;
using Koan.Testing.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// Full-DI bootstrap smoke for the cache pillar. Unlike unit tests and even the per-adapter
/// integration tests (which manually invoke <c>new KoanAutoRegistrar().Initialize(services)</c>),
/// these specs go through <c>services.AddKoan()</c> — the real reflective discovery path that
/// production code uses. They prove the "Reference = Intent" headline promise end-to-end.
/// </summary>
/// <remarks>
/// <para>
/// References cover the cache pillar's Tier 1 surface: <c>Koan.Cache</c> (Memory L1 +
/// orchestration), <c>Koan.Cache.Adapter.Sqlite</c> (Local L1 priority 50, preempts Memory),
/// <c>Koan.Cache.Adapter.Redis</c> (Remote L2 + coherence channel), and
/// <c>Koan.Cache.Coherence.InMemory</c> (fallback channel). <c>Koan.Cache.Coherence.Messaging</c>
/// is intentionally excluded — it requires <c>IMessageBus</c> to be wired, which is out of
/// scope for cache-pillar boot smoke and deferred to a Tier 2 messaging smoke.
/// </para>
/// <para>
/// All tests require Docker because <c>RedisCacheStore</c> resolves <c>IConnectionMultiplexer</c>
/// eagerly during topology construction. Without a real Redis daemon, <c>BuildServiceProvider</c>
/// + first ICacheStore enumeration would throw at connect time.
/// </para>
/// </remarks>
public sealed class CachePillarBootstrapSpec
{
    private readonly ITestOutputHelper _output;

    public CachePillarBootstrapSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task AddKoan_resolves_ICacheClient_through_real_bootstrap()
        => TestPipeline.For<CachePillarBootstrapSpec>(_output, nameof(AddKoan_resolves_ICacheClient_through_real_bootstrap))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var redis = ctx.GetRedisFixture();
                if (!redis.IsAvailable || string.IsNullOrWhiteSpace(redis.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {redis.UnavailableReason ?? "unspecified"}");

                using var dbPath = CreateTempSqlitePath();

                // The "Reference = Intent" headline test: with the four cache packages
                // referenced, ICacheClient must resolve end-to-end without any explicit
                // AddKoanCache() call in user code. Going through host startup also exercises
                // the full hosted-service lifecycle, so descriptor-class bugs (e.g., the
                // TryAddEnumerable indistinguishable bug fixed in 14a5e8ce) would surface here.
                await using var host = await BuildBootstrapHost(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId).StartAsync(ctx.Cancellation);

                var client = host.Services.GetRequiredService<ICacheClient>();
                client.Should().NotBeNull();
            })
            .Run();

    [Fact]
    public Task AddKoan_registers_memory_sqlite_and_redis_as_ICacheStore()
        => TestPipeline.For<CachePillarBootstrapSpec>(_output, nameof(AddKoan_registers_memory_sqlite_and_redis_as_ICacheStore))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var redis = ctx.GetRedisFixture();
                if (!redis.IsAvailable || string.IsNullOrWhiteSpace(redis.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {redis.UnavailableReason ?? "unspecified"}");

                using var dbPath = CreateTempSqlitePath();
                await using var host = await BuildBootstrapHost(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId).StartAsync(ctx.Cancellation);

                var stores = host.Services.GetServices<ICacheStore>().ToList();
                stores.Should().HaveCountGreaterThanOrEqualTo(3, "Memory + Sqlite + Redis are referenced");

                var storeNames = stores.Select(s => s.Name).ToList();
                storeNames.Should().Contain("memory");
                storeNames.Should().Contain("sqlite");
                storeNames.Should().Contain("redis");
            })
            .Run();

    [Fact]
    public Task AddKoan_activates_coherence_via_redis_channel_end_to_end()
        => TestPipeline.For<CachePillarBootstrapSpec>(_output, nameof(AddKoan_activates_coherence_via_redis_channel_end_to_end))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var redis = ctx.GetRedisFixture();
                if (!redis.IsAvailable || string.IsNullOrWhiteSpace(redis.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {redis.UnavailableReason ?? "unspecified"}");

                using var dbPath = CreateTempSqlitePath();
                await using var host = await BuildBootstrapHost(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId).StartAsync(ctx.Cancellation);

                // Indirect activation proof: a write must successfully complete its full
                // broadcast path (L1 + L2 + Coherence). If any link is broken, this throws.
                var client = host.Services.GetRequiredService<ICacheClient>();
                var key = new CacheKey($"bootstrap-coherence-{ctx.ExecutionId:N}");
                await client.CreateEntry<string>(key)
                    .WithAbsoluteTtl(TimeSpan.FromMinutes(1))
                    .Set("smoke", ctx.Cancellation);

                var value = await client.CreateEntry<string>(key).Get(ctx.Cancellation);
                value.Should().Be("smoke");
            })
            .Run();

    /// <summary>
    /// Build a fully-configured <see cref="KoanIntegrationHost.Builder"/> for the cache
    /// pillar's bootstrap smoke. Tests call <c>.StartAsync(ct)</c> on the returned builder
    /// to get a started <see cref="IntegrationHost"/>.
    /// </summary>
    /// <remarks>
    /// <b>Cross-pillar workaround:</b> referencing <c>Koan.Cache.Adapter.Redis</c> also pulls
    /// <c>Koan.Data.Connector.Redis</c> in transitively. Both register
    /// <c>IConnectionMultiplexer</c> at boot — the data connector via <c>AddSingleton</c>
    /// (forced), the cache adapter via <c>TryAddSingleton</c> (skipped if present). The data
    /// connector's registration therefore wins, and it reads its connection string from
    /// <c>Koan:Data:Redis:ConnectionString</c> — NOT from <c>Cache:Redis:Configuration</c>.
    /// Apps using both pillars MUST set both keys to the same value, or the cache silently
    /// connects to the wrong Redis. ARCH-0080 (pending) will codify the shared-transport
    /// ownership pattern that makes this workaround unnecessary.
    /// </remarks>
    private static KoanIntegrationHost.Builder BuildBootstrapHost(string redisConnectionString, string sqliteDbPath, Guid executionId)
    {
        var token = executionId.ToString("N");
        return KoanIntegrationHost.Configure()
            .WithSetting(CacheConstants.Configuration.Redis.Configuration, redisConnectionString)
            .WithSetting(CacheConstants.Configuration.Redis.KeyPrefix, $"boot:{token}:")
            .WithSetting(CacheConstants.Configuration.Redis.TagPrefix, $"boot:tag:{token}:")
            .WithSetting(CacheConstants.Configuration.Redis.ChannelName, $"boot-cache-{token}")
            .WithSetting("Koan:Data:Redis:ConnectionString", redisConnectionString)
            .WithSetting("Koan:Data:Redis:DisableAutoDetection", "true")
            .WithSetting("ConnectionStrings:Redis", redisConnectionString)
            .WithSetting("Koan:Cache:Adapters:Sqlite:DatabasePath", sqliteDbPath)
            .WithSetting("Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds", "3600")
            .ConfigureServices(services => services.AddKoan());
    }

    private static TempSqliteFile CreateTempSqlitePath()
        => new(Path.Combine(Path.GetTempPath(), $"koan-boot-smoke-{Guid.NewGuid():N}.db"));

    private sealed class TempSqliteFile : IDisposable
    {
        public TempSqliteFile(string path) { Path = path; }
        public string Path { get; }
        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { /* best-effort */ }
        }
    }
}
