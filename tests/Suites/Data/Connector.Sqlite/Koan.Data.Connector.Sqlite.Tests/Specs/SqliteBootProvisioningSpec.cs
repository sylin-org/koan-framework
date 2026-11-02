using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Lexical;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// ARCH-0142: every discovered entity on a route that holds has its table before the host reports started, so nothing
/// changes the schema afterward, which is what a new connection's first use of an FTS5 table cannot survive, and no table
/// is first made inside a hold.
/// </summary>
public sealed class SqliteBootProvisioningSpec
{
    public sealed class UntouchedNote : Entity<UntouchedNote>
    {
        [Lexical] public string Title { get; set; } = "";
    }

    public sealed class UntouchedCount : Entity<UntouchedCount, int>
    {
        public string Label { get; set; } = "";
    }

    private static string NewFile() => Path.Combine(Path.GetTempPath(), $"koan-sqlite-boot-{Guid.CreateVersion7():n}.db");

    private static Task<IntegrationHost> Boot(string file, string ddl = "AutoCreate") => KoanIntegrationHost.Configure()
        .WithSetting("Koan:Environment", "Test")
        .WithSetting("Koan:Data:Sqlite:DdlPolicy", ddl)
        .WithSetting("Koan:Data:Sqlite:ConnectionString", $"Data Source={file}")
        .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
        .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={file}")
        .ConfigureServices(services => services.AddKoan())
        .StartAsync();

    private static async Task End(IntegrationHost host, string file)
    {
        await host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (File.Exists(file)) File.Delete(file);
    }

    private static long SchemaVersion(string file)
    {
        using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA schema_version";
        return (long)command.ExecuteScalar()!;
    }

    private static bool Holds(string file, string nameLike)
    {
        using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE name LIKE @name";
        command.Parameters.AddWithValue("@name", nameLike);
        return (long)command.ExecuteScalar()! > 0;
    }

    [Fact(DisplayName = "Sqlite: an entity nobody used has its table and its index when the host reports started")]
    public async Task An_entity_nobody_used_has_its_table_at_start()
    {
        var file = NewFile();
        var host = await Boot(file);
        try
        {
            Holds(file, "%UntouchedNote").Should().BeTrue();
            Holds(file, "%UntouchedNote__koan_lexical").Should().BeTrue();
            Holds(file, "%UntouchedCount").Should().BeTrue("a key that is not a string is a key");
            // The first use of the index is a search, on a connection of its own, and answers.
            SqliteConnection.ClearAllPools();
            (await UntouchedNote.Lexical.Search("anything", query => query.Top(5))).Should().BeEmpty();
        }
        finally
        {
            await End(host, file);
        }
    }

    [Fact(DisplayName = "Sqlite: nothing changes the schema after the host reports started")]
    public async Task Nothing_changes_the_schema_after_start()
    {
        var file = NewFile();
        var host = await Boot(file);
        try
        {
            var atStart = SchemaVersion(file);
            // A host's first lexical measurement and its workers run just after start, which is when tables used to appear.
            await Task.Delay(750, TestContext.Current.CancellationToken);
            await new UntouchedNote { Title = "copper lanterns" }.Save(TestContext.Current.CancellationToken);
            SchemaVersion(file).Should().Be(atStart);
        }
        finally
        {
            await End(host, file);
        }
    }

    [Fact(DisplayName = "Sqlite: a file restored without one of its tables has it back at start")]
    public async Task A_restored_file_missing_a_table_has_it_at_start()
    {
        var file = NewFile();
        var first = await Boot(file);
        await first.DisposeAsync();
        SqliteConnection.ClearAllPools();
        using (var raw = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            raw.Open();
            using var drop = raw.CreateCommand();
            drop.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE '%UntouchedCount'";
            var name = (string)drop.ExecuteScalar()!;
            drop.CommandText = $"DROP TABLE \"{name}\"";
            drop.ExecuteNonQuery();
        }

        var host = await Boot(file);
        try
        {
            Holds(file, "%UntouchedCount").Should().BeTrue();
        }
        finally
        {
            await End(host, file);
        }
    }

    [Fact(DisplayName = "Sqlite: a store that refuses DDL does not stop the boot, and answers at first use as it would have")]
    public async Task A_store_that_refuses_ddl_does_not_stop_the_boot()
    {
        var file = NewFile();
        var host = await Boot(file, ddl: "NoDdl");
        try
        {
            var firstUse = () => UntouchedNote.Get("anything", TestContext.Current.CancellationToken);
            await firstUse.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await End(host, file);
        }
    }
}
