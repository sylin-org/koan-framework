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
            await using var drop = conn.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS \"{name}\"";
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
