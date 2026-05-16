using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Extensions;
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
        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch
        {
            // ignored — best-effort cleanup
        }
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

        await client.CreateEntry<string>(new CacheKey("disk-canary"))
            .WithAbsoluteTtl(TimeSpan.FromMinutes(5))
            .Set("value", ct);

        File.Exists(_databasePath).Should().BeTrue("SQLite adapter must create the database file");
        new FileInfo(_databasePath).Length.Should().BeGreaterThan(0, "the database should contain pages");
    }

    /// <summary>
    /// Rides <see cref="KoanIntegrationHost"/> (the ARCH-0079 canon helper). Manual adapter
    /// activation is intentional — this spec exercises Sqlite adapter behavior in isolation;
    /// reflective discovery is covered by <c>CachePillarBootstrapSpec</c>.
    /// </summary>
    private static Task<IntegrationHost> BuildNode(string databasePath, CancellationToken ct)
        => KoanIntegrationHost.Configure()
            .WithSetting("Koan:Cache:Adapters:Sqlite:DatabasePath", databasePath)
            // Disable the background sweeper for tests — keeps the lifecycle deterministic.
            .WithSetting("Koan:Cache:Adapters:Sqlite:SweepIntervalSeconds", "3600")
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddKoanCache();
                new Koan.Cache.Adapter.Sqlite.Initialization.KoanAutoRegistrar().Initialize(services);
            })
            .StartAsync(ct);
}
