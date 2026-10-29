using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Core.Provenance;
using Koan.Data.Connector.Sqlite.Initialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0158: the posture the connector gives every connection it opens — journal mode, busy timeout, synchronous —
/// is configured in one place, applied on open, written to the database file where SQLite keeps it, and reported by
/// the startup facts and readiness so an operator sees the mode in effect.
/// </summary>
public sealed class SqlitePostureSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    public sealed class PostureRecord : Entity<PostureRecord>
    {
        public string Value { get; set; } = "";
    }

    [Fact]
    public async Task A_fresh_file_database_is_write_ahead_logged_by_default()
    {
        RequireBackingStore();
        var path = TempDatabase("wal-default");
        try
        {
            await using (var host = await BootAsync(Settings(path)))
            {
                using var _ = Lease(NewPartition("wal-default"));
                await new PostureRecord { Value = "first write" }.Save();
            }

            // Bytes 18 and 19 of the header are the file format versions: 1,1 for a rollback journal, 2,2 for WAL.
            ReadHeaderFormat(path).Should().Be((2, 2), "WAL is the connector's default journal mode");
            (await ReadJournalMode(path)).Should().Be("wal");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_configured_journal_mode_is_the_one_the_database_holds()
    {
        RequireBackingStore();
        var path = TempDatabase("delete-configured");
        try
        {
            var settings = Settings(path);
            settings["Koan:Data:Sqlite:JournalMode"] = "Delete";
            await using (var host = await BootAsync(settings))
            {
                using var _ = Lease(NewPartition("delete-configured"));
                await new PostureRecord { Value = "rollback journal" }.Save();
            }

            ReadHeaderFormat(path).Should().Be((1, 1));
            (await ReadJournalMode(path)).Should().Be("delete");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task Every_connection_the_connector_hands_out_carries_the_busy_timeout_and_synchronous_level()
    {
        RequireBackingStore();
        var path = TempDatabase("pragmas");
        try
        {
            var settings = Settings(path);
            settings["Koan:Data:Sqlite:BusyTimeout"] = "00:00:07";
            settings["Koan:Data:Sqlite:Synchronous"] = "Normal";
            await using var host = await BootAsync(settings);
            using var _ = Lease(NewPartition("pragmas"));
            await new PostureRecord { Value = "opened" }.Save();

            // A Direct session opens the connection itself, so what it reads back is what every open gets.
            var direct = host.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");
            (await direct.Scalar<long>("PRAGMA busy_timeout")).Should().Be(7_000);
            (await direct.Scalar<long>("PRAGMA synchronous")).Should().Be(1, "NORMAL is 1, FULL is 2");
            (await direct.Scalar<string>("PRAGMA journal_mode")).Should().Be("wal");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task Two_hundred_simultaneous_saves_all_succeed()
    {
        RequireBackingStore();
        var path = TempDatabase("concurrent");
        try
        {
            await using var host = await BootAsync(Settings(path));
            using var _ = Lease(NewPartition("concurrent"));
            await new PostureRecord { Value = "warm" }.Save();

            await Task.WhenAll(Enumerable.Range(0, 200)
                .Select(index => Task.Run(() => new PostureRecord { Value = $"row-{index}" }.Save())));

            (await PostureRecord.Count).Should().Be(201);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_lock_wait_ends_at_the_configured_busy_timeout_not_the_drivers()
    {
        RequireBackingStore();
        var path = TempDatabase("busy");
        try
        {
            var settings = Settings(path);
            settings["Koan:Data:Sqlite:BusyTimeout"] = "00:00:02";
            await using var host = await BootAsync(settings);
            using var _ = Lease(NewPartition("busy"));
            await new PostureRecord { Value = "provisioned" }.Save();

            // Another connection holds the write lock for the whole attempt.
            await using var holder = new SqliteConnection($"Data Source={path};Pooling=False");
            await holder.OpenAsync();
            await using (var begin = holder.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                await begin.ExecuteNonQueryAsync();
            }

            var clock = Stopwatch.StartNew();
            var save = () => new PostureRecord { Value = "blocked" }.Save();
            await save.Should().ThrowAsync<Exception>();
            clock.Stop();

            clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.5), "the wait honours the 2 s timeout");
            clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
                "the driver's own 30 s retry must be bounded by the same option, not only SQLite's busy handler");

            await using var rollback = holder.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task A_misspelt_posture_value_is_refused_with_the_key_and_the_choices()
    {
        RequireBackingStore();
        var path = TempDatabase("misspelt");
        try
        {
            var settings = Settings(path);
            settings["Koan:Data:Sqlite:JournalMode"] = "Wally";

            // The options bind at boot, so the host refuses to start rather than running on a posture it cannot honour.
            var boot = async () => { await using var host = await BootAsync(settings); };
            var refusal = (await boot.Should().ThrowAsync<InvalidOperationException>()).Which;
            refusal.Message.Should().Contain("Koan:Data:Sqlite:JournalMode").And.Contain("Wally").And.Contain("Wal");
            File.Exists(path).Should().BeFalse("a refused posture opens nothing");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task Readiness_reports_the_posture_and_the_journal_mode_each_source_holds()
    {
        RequireBackingStore();
        var path = TempDatabase("readiness");
        try
        {
            await using var host = await BootAsync(Settings(path));
            using var _ = Lease(NewPartition("readiness"));
            await new PostureRecord { Value = "provisioned" }.Save();

            var contributor = host.Services.GetServices<IHealthContributor>()
                .Single(candidate => candidate.Name == "data:sqlite");
            var report = await contributor.Check();

            report.State.Should().Be(HealthState.Healthy);
            report.Data.Should().ContainKey("journalMode").WhoseValue.Should().Be("WAL");
            report.Data.Should().ContainKey("busyTimeout").WhoseValue.Should().Be("00:00:30");
            report.Data.Should().ContainKey("synchronous").WhoseValue.Should().Be("FULL");
            report.Data.Should().ContainKey("journalModes").WhoseValue.Should().Be("Default=wal");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Startup_facts_report_the_three_posture_settings_with_their_source()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-sqlite-posture-facts-{Guid.CreateVersion7():n}");
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Data:Sqlite:Synchronous"] = "Normal"
                })
                .Build();
            var registry = ProvenanceRegistry.Instance;
            var module = registry.GetOrCreateModule("data", "Koan.Data.Connector.Sqlite");

            new SqliteModule().Report(module, configuration, new TestHostEnvironment(root));

            var settings = registry.CurrentSnapshot.FindModule("data", "Koan.Data.Connector.Sqlite")!.Settings;
            settings.Single(setting => setting.Key == "Koan:Data:Sqlite:JournalMode").Value.Should().Be("Wal");
            settings.Single(setting => setting.Key == "Koan:Data:Sqlite:BusyTimeout").Value.Should().Be("00:00:30");
            var synchronous = settings.Single(setting => setting.Key == "Koan:Data:Sqlite:Synchronous");
            synchronous.Value.Should().Be("Normal");
            synchronous.Source.Should().NotBe(ProvenanceSettingSource.Auto, "a configured value is not a default");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, string?> Settings(string path) => new(StringComparer.Ordinal)
    {
        ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
        ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}",
    };

    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-sqlite-posture-{label}-{Guid.CreateVersion7():n}.db");

    private static (byte Write, byte Read) ReadHeaderFormat(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var header = new byte[20];
        stream.ReadExactly(header, 0, header.Length);
        return (header[18], header[19]);
    }

    private static async Task<string?> ReadJournalMode(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        return await command.ExecuteScalarAsync() as string;
    }

    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            if (File.Exists(file)) File.Delete(file);
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Connector.Sqlite.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
