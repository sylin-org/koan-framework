using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Core;
using Koan.Data.Relational.Tests;

namespace Koan.Data.Connector.Sqlite.Tests;

public class SqliteAutoFixture : IRelationalTestFixture<SqliteSchemaGovernanceSharedTests.Todo, string>, Xunit.IAsyncLifetime
{
    public IDataService Data { get; private set; } = default!;
    public IServiceProvider ServiceProvider { get; private set; } = default!;
    public bool SkipTests { get; private set; } = false;
    public string? SkipReason { get; private set; }
    private string _dbFile = default!;

    public async Task InitializeAsync()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), "Koan-sqlite-shared-" + Guid.NewGuid().ToString("n") + ".db");
        var cs = $"Data Source={_dbFile}";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Koan:Data:Sqlite:ConnectionString", cs),
                new KeyValuePair<string, string?>("Koan_DATA_PROVIDER", "sqlite"),
                new KeyValuePair<string, string?>("Koan:AllowMagicInProduction", "true"),
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddSqliteAdapter(o =>
        {
            o.ConnectionString = cs;
            o.DdlPolicy = SchemaDdlPolicy.AutoCreate;
            o.AllowProductionDdl = true;
        });
        sc.AddKoanDataCore();
        sc.AddSingleton<IDataService, DataService>();
        ServiceProvider = sc.BuildServiceProvider();
        Data = ServiceProvider.GetRequiredService<IDataService>();
        // rely on adapter DDL policy (AutoCreate) to create the schema during tests
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_dbFile) && File.Exists(_dbFile))
        {
            try { File.Delete(_dbFile); } catch { }
        }
        await Task.CompletedTask;
    }
}

