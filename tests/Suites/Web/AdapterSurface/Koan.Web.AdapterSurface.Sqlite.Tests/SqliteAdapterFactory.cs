using Microsoft.Data.Sqlite;
using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

public sealed class SqliteAdapterFactory : AdapterTestFactoryBase
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"koan-surface-sqlite-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public override bool IsAvailable => true;

    protected override IEnumerable<KeyValuePair<string, string?>> AdapterConfiguration() => new Dictionary<string, string?>
    {
        ["Koan:Environment"] = "Development",
        ["Koan:AllowMagicInProduction"] = "true",
        ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
        ["Koan:Data:Sources:Default:ConnectionString"] = ConnectionString,
        ["Koan:Data:Sqlite:ConnectionString"] = ConnectionString,
        ["Koan:Data:Sqlite:DdlPolicy"] = "AutoCreate",
        ["Koan:Data:Relational:Materialization:FailOnMismatch"] = "false",
        ["Koan:BackgroundServices:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning",
    };

    protected override ValueTask StopBackingStoreAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears rows, and deliberately does not drop tables.
    ///
    /// <para>Provisioning is memoized per host by <c>DataSourceReadinessCoordinator</c> — Koan's
    /// "compile once per host shape" law. Dropping a table out of band leaves the host believing it
    /// still exists, so every later spec died on a raw driver error ("no such table"). A between-spec
    /// reset wants the data gone, not the schema; that is what InMemory's <c>Widget.RemoveAll()</c>
    /// already does. A test that genuinely needs a schema reset must call
    /// <c>DataSourceReadinessCoordinator.InvalidateShape</c>, the sanctioned way to declare an
    /// authorized shape change.</para>
    /// </summary>
    public override async Task ResetAsync()
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        var names = new List<string>();
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var rdr = await read.ExecuteReaderAsync().ConfigureAwait(false);
            while (await rdr.ReadAsync().ConfigureAwait(false))
            {
                names.Add(rdr.GetString(0));
            }
        }
        foreach (var name in names)
        {
            await using var clear = conn.CreateCommand();
            clear.CommandText = $"DELETE FROM \"{name}\"";
            await clear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
