using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Web.AdapterSurface.TestKit;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

public sealed class SqliteAdapterFactory : WebApplicationFactory<Program>, IAdapterTestFactory
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public HttpClient Client => CreateClient();
    public new IServiceProvider Services => base.Services;

    public SqliteAdapterFactory()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"koan-surface-sqlite-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }

    public async Task ResetAsync()
    {
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
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
        catch { /* best effort */ }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseContentRoot(AppContext.BaseDirectory);
            webBuilder.UseTestServer();
            webBuilder.UseEnvironment("Development");
            webBuilder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Koan:Environment"] = "Development",
                    ["Koan:AllowMagicInProduction"] = "true",
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Default:ConnectionString"] = _connectionString,
                    ["Koan:Data:Sqlite:ConnectionString"] = _connectionString,
                    ["Koan:Data:Sqlite:DdlPolicy"] = "AutoCreate",
                    ["Koan:Data:Relational:Materialization:FailOnMismatch"] = "false",
                    ["Koan:BackgroundServices:Enabled"] = "false",
                    ["Logging:LogLevel:Default"] = "Warning",
                });
            });
            webBuilder.ConfigureServices(_ => { Koan.Core.Hosting.App.AppHost.Current = null; });
        });
        var host = builder.Build();
        host.Start();
        return host;
    }
}
