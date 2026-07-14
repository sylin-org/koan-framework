using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core;
using Koan.Testing.Containers;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Infrastructure.Specs;

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
/// ARCH-0091: Redis comes from the shared <see cref="RedisFixture"/> as a class fixture — the
/// container starts once for this class only (the sibling offline boot-smoke specs never pay for
/// it). All tests require Docker because <c>RedisCacheStore</c> resolves <c>IConnectionMultiplexer</c>
/// eagerly during topology construction; without a real Redis daemon the fixture is unavailable and
/// these specs <c>Assert.Skip</c>.
/// </para>
/// </remarks>
public sealed class CachePillarBootstrapSpec(RedisFixture redis, ITestOutputHelper output) : IClassFixture<RedisFixture>
{
    private readonly ITestOutputHelper _output = output;

    [Fact(Explicit = true)]
    public async Task AddKoan_resolves_ICacheClient_through_real_bootstrap()
    {
        Assert.SkipWhen(!redis.IsAvailable, redis.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;
        var executionId = Guid.CreateVersion7();

        using var dbPath = CreateTempSqlitePath();

        // The "Reference = Intent" headline test: with the four cache packages referenced,
        // ICacheClient must resolve end-to-end without any explicit AddKoanCache() call in user
        // code. Going through host startup also exercises the full hosted-service lifecycle, so
        // descriptor-class bugs (e.g., the TryAddEnumerable indistinguishable bug fixed in 14a5e8ce)
        // would surface here.
        await using var host = await BuildBootstrapHost(redis.ConnectionString!, dbPath.Path, executionId).StartAsync(ct);

        var client = host.Services.GetRequiredService<ICacheClient>();
        client.Should().NotBeNull();
    }

    [Fact(Explicit = true)]
    public async Task AddKoan_registers_memory_sqlite_and_redis_as_ICacheStore()
    {
        Assert.SkipWhen(!redis.IsAvailable, redis.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;
        var executionId = Guid.CreateVersion7();

        using var dbPath = CreateTempSqlitePath();
        await using var host = await BuildBootstrapHost(redis.ConnectionString!, dbPath.Path, executionId).StartAsync(ct);

        var stores = host.Services.GetServices<ICacheStore>().ToList();
        stores.Should().HaveCountGreaterThanOrEqualTo(3, "Memory + Sqlite + Redis are referenced");

        var storeNames = stores.Select(s => s.Name).ToList();
        storeNames.Should().Contain("memory");
        storeNames.Should().Contain("sqlite");
        storeNames.Should().Contain("redis");
    }

    [Fact(Explicit = true)]
    public async Task AddKoan_activates_coherence_via_redis_channel_end_to_end()
    {
        Assert.SkipWhen(!redis.IsAvailable, redis.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;
        var executionId = Guid.CreateVersion7();

        using var dbPath = CreateTempSqlitePath();
        await using var host = await BuildBootstrapHost(redis.ConnectionString!, dbPath.Path, executionId).StartAsync(ct);

        // Indirect activation proof: a write must successfully complete its full broadcast path
        // (L1 + L2 + Coherence). If any link is broken, this throws. The cache key shares the host's
        // execution id, so the write/read roundtrip stays inside this test's partition (key prefix).
        var client = host.Services.GetRequiredService<ICacheClient>();
        var key = new CacheKey($"bootstrap-coherence-{executionId:N}");
        await client.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMinutes(1))
            .Set("smoke", ct);

        var value = await client.CreateEntry<string>(key).Get(ct);
        value.Should().Be("smoke");
    }

    /// <summary>
    /// Build a fully-configured <see cref="KoanIntegrationHost.Builder"/> for the cache pillar's
    /// bootstrap smoke. Tests call <c>.StartAsync(ct)</c> on the returned builder to get a started host.
    /// </summary>
    /// <remarks>
    /// Per ARCH-0080, <c>IConnectionMultiplexer</c> is owned by <c>Koan.Data.Connector.Redis</c> and
    /// reads from the canonical <c>Koan:Data:Redis:ConnectionString</c> key. The cache adapter consumes
    /// the multiplexer via DI and only owns cache-specific options (key/tag prefixes, channel name).
    /// </remarks>
    private static KoanIntegrationHost.Builder BuildBootstrapHost(string redisConnectionString, string sqliteDbPath, Guid executionId)
    {
        var token = executionId.ToString("N");
        return KoanIntegrationHost.Configure()
            // ARCH-0080: data connector owns IConnectionMultiplexer; this is the canonical key.
            .WithSetting("Koan:Data:Redis:ConnectionString", redisConnectionString)
            .WithSetting("Koan:Data:Redis:DisableAutoDetection", "true")
            // Cache-adapter-owned options (prefixes + channel name).
            .WithSetting(CacheConstants.Configuration.Redis.KeyPrefix, $"boot:{token}:")
            .WithSetting(CacheConstants.Configuration.Redis.TagPrefix, $"boot:tag:{token}:")
            .WithSetting(CacheConstants.Configuration.Redis.ChannelName, $"boot-cache-{token}")
            // SQLite adapter config.
            .WithSetting("Koan:Cache:Adapters:Sqlite:DatabasePath", sqliteDbPath)
            .WithSetting("Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds", "3600")
            .ConfigureServices(services => services.AddKoan());
    }

    private static TempSqliteFile CreateTempSqlitePath()
        => new(Path.Combine(Path.GetTempPath(), $"koan-boot-smoke-{Guid.CreateVersion7():N}.db"));

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
