using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Cache.Adapter.Sqlite.Tests.Specs;

/// <summary>
/// Integration test for the SQLite cache adapter. Proves data written by one
/// <c>IServiceProvider</c> survives complete disposal and rehydration by a second
/// <c>IServiceProvider</c> pointed at the same database file — the persistence story
/// the Sqlite adapter is meant to provide on top of the in-memory default.
/// </summary>
/// <remarks>
/// This is a real integration test (not unit) — it touches the file system, exercises the
/// SQLite driver, and proves the adapter's auto-registrar wires storage correctly. Test
/// uses a fresh temp file per test method so concurrent test runs don't collide.
/// </remarks>
public sealed class SqliteCachePersistenceSpec : IDisposable
{
    private readonly string _databasePath;

    public SqliteCachePersistenceSpec()
    {
        // Use a unique temp file per test instance — Sqlite locks the file while open.
        _databasePath = Path.Combine(Path.GetTempPath(), $"koan-cache-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    [Fact]
    public async Task Cached_entries_survive_provider_disposal_and_rehydration()
    {
        var ct = CancellationToken.None;
        var key = new CacheKey("persistence-canary");

        // ── Lifetime 1: write the entry, then dispose the whole DI graph including SQLite. ─
        await using (var node = await BuildNode(_databasePath, ct))
        {
            var client = node.Services.GetRequiredService<ICacheClient>();
            await client.CreateEntry<string>(key)
                .WithAbsoluteTtl(TimeSpan.FromMinutes(10))
                .Set("payload", ct);

            // Sanity: same provider can read it back.
            var sameLifetime = await client.CreateEntry<string>(key).Get(ct);
            sameLifetime.Should().Be("payload");
        }

        // ── Lifetime 2: brand-new provider, same database file. ─
        // L1 (Memory) starts empty; if the value is found, it MUST be from SQLite — proving
        // the persistence promise. If the test passes against a fresh process, persistence
        // is real and not just "Memory L1 retained somehow".
        await using (var node = await BuildNode(_databasePath, ct))
        {
            var client = node.Services.GetRequiredService<ICacheClient>();
            var afterRehydration = await client.CreateEntry<string>(key).Get(ct);
            afterRehydration.Should().Be("payload");
        }
    }

    [Fact]
    public async Task Cache_writes_create_an_actual_sqlite_database_file()
    {
        // Proves the SQLite adapter is actually engaged — not just the in-memory L1
        // returning hits that happen to match. After a write, the .db file should exist
        // on disk with non-zero size.
        var ct = CancellationToken.None;

        await using var node = await BuildNode(_databasePath, ct);
        var client = node.Services.GetRequiredService<ICacheClient>();
        var defaults = client.CreateEntry<string>(new CacheKey("defaults-canary")).Options;
        defaults.AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(300));
        defaults.Tier.Should().Be(CacheTier.Layered);
        defaults.ForceCoherenceBroadcast.Should().BeTrue();

        await client.CreateEntry<string>(new CacheKey("disk-canary"))
            .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
            .Set("value", ct);

        File.Exists(_databasePath).Should().BeTrue("SQLite adapter must create the database file");
        new FileInfo(_databasePath).Length.Should().BeGreaterThan(0, "the database should contain pages");
    }

    [Fact]
    public async Task Tag_enumeration_matches_whole_tags_not_substrings()
    {
        var ct = CancellationToken.None;
        await using var node = await BuildNode(_databasePath, ct);
        var client = node.Services.GetRequiredService<ICacheClient>();

        await client.CreateEntry<string>(new CacheKey("order"))
            .WithTags("order")
            .Set("one", ct);
        await client.CreateEntry<string>(new CacheKey("order-archive"))
            .WithTags("order-archive")
            .Set("two", ct);
        await client.CreateEntry<string>(new CacheKey("order-priority"))
            .WithTags("order,priority")
            .Set("three", ct);

        var sqlite = node.Services.GetServices<ICacheStore>().Single(store => store.Name == "sqlite");
        var keys = new List<string>();
        await foreach (var entry in sqlite.EnumerateByTag("order", ct))
            keys.Add(entry.Key.Value);

        keys.Should().ContainSingle().Which.Should().Contain("order");
        keys.Should().NotContain(key => key.Contains("order-archive", StringComparison.Ordinal));

        var commaTagKeys = new List<string>();
        await foreach (var entry in sqlite.EnumerateByTag("order,priority", ct))
            commaTagKeys.Add(entry.Key.Value);
        commaTagKeys.Should().ContainSingle().Which.Should().Contain("order-priority");
    }

    [Fact]
    public async Task Sliding_expiration_is_refreshed_by_a_fresh_read()
    {
        var ct = CancellationToken.None;
        var key = new CacheKey("sliding-canary");
        await using var node = await BuildNode(_databasePath, ct);
        var client = node.Services.GetRequiredService<ICacheClient>();
        var entry = client.CreateEntry<string>(key)
            .WithTier(CacheTier.LocalOnly)
            .WithAbsoluteTtl(TimeSpan.FromMilliseconds(250))
            .WithSlidingTtl(TimeSpan.FromMilliseconds(250));

        await entry.Set("alive", ct);
        await Task.Delay(150, ct);
        (await entry.Get(ct)).Should().Be("alive");
        await Task.Delay(150, ct);

        (await entry.Get(ct)).Should().Be("alive", "the first read must renew the SQLite sliding window");
    }

    [Fact]
    public async Task Tag_count_cleans_expired_sqlite_rows_without_a_background_sweeper()
    {
        var ct = CancellationToken.None;
        var key = new CacheKey("expired-tag-canary");
        await using var node = await BuildNode(_databasePath, ct);
        var client = node.Services.GetRequiredService<ICacheClient>();
        await client.CreateEntry<string>(key)
            .WithAbsoluteTtl(TimeSpan.FromMilliseconds(50))
            .WithTags("expired")
            .Set("old", ct);
        await Task.Delay(100, ct);

        (await client.CountTags(["expired"], ct)).Should().Be(0);
        var sqlite = node.Services.GetServices<ICacheStore>().Single(store => store.Name == "sqlite");
        var remaining = new List<TaggedCacheKey>();
        await foreach (var tagged in sqlite.EnumerateByTag("expired", ct))
            remaining.Add(tagged);
        remaining.Should().BeEmpty("the runtime removes expired tag matches from SQLite");
    }

    /// <summary>
    /// Rides <see cref="KoanIntegrationHost"/> (the ARCH-0079 canon helper). Manual adapter
    /// activation follows the public <c>AddKoan()</c> path, proving Reference = Intent for this
    /// isolated Cache + SQLite package graph.
    /// </summary>
    private static Task<IntegrationHost> BuildNode(string databasePath, CancellationToken ct)
        => KoanIntegrationHost.Configure()
            .WithSetting("Koan:Cache:Adapters:Sqlite:DatabasePath", databasePath)
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddKoan();
            })
            .StartAsync(ct);
}
