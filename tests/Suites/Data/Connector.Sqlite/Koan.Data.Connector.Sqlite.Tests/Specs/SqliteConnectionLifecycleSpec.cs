using System.Collections.Generic;
using System.IO;
using Koan.Data.Core.Direct;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite connection ownership contracts. A Koan host owns the connections it opens: disposing that host must
/// release its exact database immediately, and the private in-memory shorthand must remain useful across the
/// repository's per-operation connections for the host's lifetime.
/// </summary>
public sealed class SqliteConnectionLifecycleSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    public sealed class LifecycleRecord : Entity<LifecycleRecord>
    {
        public string Value { get; set; } = "";
    }

    [Fact]
    public async Task Host_disposal_releases_the_exact_database_before_another_host_reuses_its_path()
    {
        RequireBackingStore();
        var databasePath = Path.Combine(Path.GetTempPath(), $"koan-sqlite-lifecycle-{Guid.CreateVersion7():n}.db");
        var partition = NewPartition("connection-lifecycle");
        string? id = null;
        var settings = Settings($"Data Source={databasePath};Pooling=True");

        await using (var first = await BootAsync(settings))
        {
            using var _ = Lease(partition);
            var saved = await new LifecycleRecord { Value = "host-a" }.Save();
            id = saved.Id;
            (await LifecycleRecord.Get(id!))!.Value.Should().Be("host-a");
            File.Exists(databasePath).Should().BeTrue();
        }

        File.Delete(databasePath);
        File.Exists(databasePath).Should().BeFalse();

        await using (var second = await BootAsync(settings))
        {
            using var _ = Lease(partition);
            (await LifecycleRecord.Get(id!)).Should().BeNull("the recreated file belongs to a new host lifetime");
            await new LifecycleRecord { Id = id!, Value = "host-b" }.Save();
            (await LifecycleRecord.Get(id!))!.Value.Should().Be("host-b");
        }

        File.Delete(databasePath);
        File.Exists(databasePath).Should().BeFalse();
    }

    [Fact]
    public async Task Private_memory_database_survives_per_operation_connections_for_one_host()
    {
        RequireBackingStore();
        await using var host = await BootAsync(Settings("Data Source=:memory:"));
        using var _ = Lease(NewPartition("private-memory"));

        var record = await new LifecycleRecord { Value = "still-here" }.Save();

        (await LifecycleRecord.Get(record.Id))!.Value.Should().Be("still-here");
    }

    [Fact]
    public async Task Named_memory_mode_survives_per_operation_connections_for_one_host()
    {
        RequireBackingStore();
        await using var host = await BootAsync(Settings("Data Source=koan-memory;Mode=Memory;Cache=Shared"));
        using var _ = Lease(NewPartition("named-memory"));

        var record = await new LifecycleRecord { Value = "named-and-still-here" }.Save();

        (await LifecycleRecord.Get(record.Id))!.Value.Should().Be("named-and-still-here");
    }

    [Fact]
    public async Task Private_memory_database_is_isolated_per_routed_source()
    {
        RequireBackingStore();
        var settings = new Dictionary<string, string?>(Settings("Data Source=:memory:"), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Archive:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Archive:ConnectionString"] = "Data Source=:memory:",
        };
        await using var host = await BootAsync(settings);
        using var _ = Lease(NewPartition("private-memory-sources"));

        var primary = await new LifecycleRecord { Value = "primary" }.Save();
        LifecycleRecord archive;
        using (EntityContext.Source("Archive"))
        {
            (await LifecycleRecord.Get(primary.Id)).Should().BeNull();
            archive = await new LifecycleRecord { Value = "archive" }.Save();
        }

        (await LifecycleRecord.Get(primary.Id))!.Value.Should().Be("primary");
        (await LifecycleRecord.Get(archive.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Identical_private_memory_configuration_is_isolated_between_live_hosts()
    {
        RequireBackingStore();
        var settings = Settings("Data Source=:memory:");
        await using var first = await BootAsync(settings);
        await using var second = await BootAsync(settings);
        var firstDirect = first.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");
        var secondDirect = second.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");

        await firstDirect.Execute("CREATE TABLE host_memory_probe (value TEXT NOT NULL)");
        await firstDirect.Execute("INSERT INTO host_memory_probe (value) VALUES ('host-a')");

        (await firstDirect.Scalar<long>("SELECT COUNT(*) FROM host_memory_probe")).Should().Be(1);
        var secondRead = await Record.ExceptionAsync(
            async () => await secondDirect.Scalar<long>("SELECT COUNT(*) FROM host_memory_probe"));
        secondRead.Should().NotBeNull(
            "the same logical source and connection intent in another host owns another memory database");
    }

    [Fact]
    public async Task Direct_private_memory_preserves_routed_source_identity()
    {
        RequireBackingStore();
        var settings = new Dictionary<string, string?>(Settings("Data Source=:memory:"), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Archive:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Archive:ConnectionString"] = "Data Source=:memory:",
        };
        await using var host = await BootAsync(settings);
        var data = host.Services.GetRequiredService<IDataService>();

        var primary = data.Direct(source: "Default");
        await primary.Execute("CREATE TABLE direct_source_probe (value TEXT NOT NULL)");
        await primary.Execute("INSERT INTO direct_source_probe (value) VALUES ('primary')");

        var archive = data.Direct(source: "Archive");
        await archive.Execute("CREATE TABLE direct_source_probe (value TEXT NOT NULL)");
        (await archive.Scalar<long>("SELECT COUNT(*) FROM direct_source_probe")).Should().Be(0);
        await archive.Execute("INSERT INTO direct_source_probe (value) VALUES ('archive')");

        (await primary.Scalar<long>("SELECT COUNT(*) FROM direct_source_probe")).Should().Be(1);
        (await archive.Scalar<long>("SELECT COUNT(*) FROM direct_source_probe")).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_sources_resolve_independent_memory_targets()
    {
        RequireBackingStore();
        const int sourceCount = 12;
        var settings = new Dictionary<string, string?>(Settings("Data Source=:memory:"), StringComparer.Ordinal);
        for (var index = 0; index < sourceCount; index++)
        {
            settings[$"Koan:Data:Sources:Source{index}:Adapter"] = "sqlite";
            settings[$"Koan:Data:Sources:Source{index}:ConnectionString"] = "Data Source=:memory:";
        }

        await using var host = await BootAsync(settings);
        var data = host.Services.GetRequiredService<IDataService>();

        await Task.WhenAll(Enumerable.Range(0, sourceCount).Select(async index =>
        {
            var direct = data.Direct(source: $"Source{index}");
            await direct.Execute("CREATE TABLE concurrent_source_probe (value INTEGER NOT NULL)");
            await direct.Execute("INSERT INTO concurrent_source_probe (value) VALUES (@value)", new { value = index });
            (await direct.Scalar<long>("SELECT COUNT(*) FROM concurrent_source_probe")).Should().Be(1);
            (await direct.Scalar<long>("SELECT value FROM concurrent_source_probe")).Should().Be(index);
        }));
    }

    private static Dictionary<string, string?> Settings(string connectionString)
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = connectionString,
        };
}
