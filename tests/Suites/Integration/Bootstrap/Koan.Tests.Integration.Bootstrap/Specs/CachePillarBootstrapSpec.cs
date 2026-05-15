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
using Koan.Testing.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public Task AddKoan_with_cache_packages_referenced_does_not_throw()
        => TestPipeline.For<CachePillarBootstrapSpec>(_output, nameof(AddKoan_with_cache_packages_referenced_does_not_throw))
            .RequireDocker()
            .UsingRedisContainer()
            .Act(async ctx =>
            {
                var redis = ctx.GetRedisFixture();
                if (!redis.IsAvailable || string.IsNullOrWhiteSpace(redis.ConnectionString))
                    throw new InvalidOperationException($"Redis unavailable: {redis.UnavailableReason ?? "unspecified"}");

                using var dbPath = CreateTempSqlitePath();
                var services = ConfigureServices(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId);

                // The smoke: AddKoan() walks every referenced assembly, finds every
                // IKoanAutoRegistrar, and calls Initialize(services). Any descriptor-class bug
                // would throw here. The TryAddEnumerable-indistinguishable bug class (fix in
                // 14a5e8ce) would have surfaced through this path.
                Action act = () => services.AddKoan();
                act.Should().NotThrow();
            })
            .Run();

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
                var services = ConfigureServices(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId);
                services.AddKoan();

                await using var sp = services.BuildServiceProvider();

                // The "Reference = Intent" headline test: with the four cache packages
                // referenced, ICacheClient must resolve end-to-end without any explicit
                // AddKoanCache() call in user code.
                var client = sp.GetRequiredService<ICacheClient>();
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
                var services = ConfigureServices(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId);
                services.AddKoan();

                await using var sp = services.BuildServiceProvider();

                var stores = sp.GetServices<ICacheStore>().ToList();
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

                // Build a real IHost — this test starts hosted services, which transitively
                // require IHostApplicationLifetime (consumed by StartupTimelineHostedService
                // registered by AddKoanCore). Bare ServiceCollection.BuildServiceProvider does
                // not provide that; HostBuilder does. This is also more faithful to production.
                using var host = BuildHost(redis.ConnectionString!, dbPath.Path, ctx.ExecutionId);

                await host.StartAsync(ctx.Cancellation).ConfigureAwait(false);
                try
                {
                    // Indirect activation proof: a write must successfully complete its full
                    // broadcast path (L1 + L2 + Coherence). If any link is broken, this throws.
                    var client = host.Services.GetRequiredService<ICacheClient>();
                    var key = new CacheKey($"bootstrap-coherence-{ctx.ExecutionId:N}");
                    await client.CreateEntry<string>(key)
                        .WithAbsoluteTtl(TimeSpan.FromMinutes(1))
                        .Set("smoke", ctx.Cancellation);

                    var value = await client.CreateEntry<string>(key).Get(ctx.Cancellation);
                    value.Should().Be("smoke");
                }
                finally
                {
                    try { await host.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch { /* teardown */ }
                }
            })
            .Run();

    /// <summary>
    /// Build a real <see cref="IHost"/> via <see cref="HostBuilder"/> so the test gets the
    /// full hosting infrastructure (lifetime, logging, configuration binding) that
    /// <c>AddKoan</c>'s hosted services depend on. Production code paths follow this pattern.
    /// </summary>
    private static IHost BuildHost(string redisConnectionString, string sqliteDbPath, Guid executionId)
    {
        var token = executionId.ToString("N");
        var settings = new Dictionary<string, string?>
        {
            [CacheConstants.Configuration.Redis.Configuration] = redisConnectionString,
            [CacheConstants.Configuration.Redis.KeyPrefix] = $"boot:{token}:",
            [CacheConstants.Configuration.Redis.TagPrefix] = $"boot:tag:{token}:",
            [CacheConstants.Configuration.Redis.ChannelName] = $"boot-cache-{token}",
            ["Koan:Data:Redis:ConnectionString"] = redisConnectionString,
            ["Koan:Data:Redis:DisableAutoDetection"] = "true",
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Koan:Cache:Adapters:Sqlite:DatabasePath"] = sqliteDbPath,
            ["Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds"] = "3600"
        };

        return new HostBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(settings))
            .ConfigureServices(services => services.AddKoan())
            .Build();
    }

    /// <summary>
    /// Builds a ServiceCollection seeded with the configuration needed by the cache adapter
    /// auto-registrars. Each test gets a unique key/tag/channel namespace plus its own
    /// temp SQLite database so test methods don't collide on shared infrastructure.
    /// </summary>
    /// <remarks>
    /// <b>Cross-pillar workaround:</b> referencing <c>Koan.Cache.Adapter.Redis</c> also pulls
    /// <c>Koan.Data.Connector.Redis</c> in transitively. Both register
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/> at boot — the data connector
    /// via <c>AddSingleton</c> (forced), the cache adapter via <c>TryAddSingleton</c> (skipped
    /// if present). The data connector's registration therefore wins, and it reads its
    /// connection string from <c>Koan:Data:Sources:Default:redis:ConnectionString</c> — NOT
    /// from <c>Cache:Redis:Configuration</c>. This means any test or production deployment
    /// that uses both pillars MUST set both keys to the same value, or the cache silently
    /// connects to the wrong Redis. Tracked as a separate cross-pillar registration bug.
    /// </remarks>
    private static IServiceCollection ConfigureServices(string redisConnectionString, string sqliteDbPath, Guid executionId)
    {
        var token = executionId.ToString("N");
        var settings = new Dictionary<string, string?>
        {
            // Cache adapter's config keys.
            [CacheConstants.Configuration.Redis.Configuration] = redisConnectionString,
            [CacheConstants.Configuration.Redis.KeyPrefix] = $"boot:{token}:",
            [CacheConstants.Configuration.Redis.TagPrefix] = $"boot:tag:{token}:",
            [CacheConstants.Configuration.Redis.ChannelName] = $"boot-cache-{token}",
            // Data connector's primary config key — read by RedisOptionsConfigurator above the
            // less specific keys. The data connector wins the IConnectionMultiplexer registration
            // race (uses AddSingleton, not TryAddSingleton), so this must point at the same Redis.
            ["Koan:Data:Redis:ConnectionString"] = redisConnectionString,
            // Disable the data connector's autonomous discovery — without this, it ignores the
            // connection string and falls back to localhost:6379 via the discovery coordinator.
            ["Koan:Data:Redis:DisableAutoDetection"] = "true",
            // Backup keys (lower priority) for any consumer reading via ConnectionStrings.
            ["ConnectionStrings:Redis"] = redisConnectionString,
            // SQLite adapter config.
            ["Koan:Cache:Adapters:Sqlite:DatabasePath"] = sqliteDbPath,
            // Disable the SQLite background sweeper — tests don't need its lifecycle noise.
            ["Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds"] = "3600"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        return services;
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
