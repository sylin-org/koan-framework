using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Relational.Tests;
using Xunit;
using Sora.Data.Core;

namespace Sora.Data.Sqlite.Tests;

public class SqliteAutoFixture : IRelationalTestFixture<SqliteSchemaGovernanceSharedTests.Todo, string>, Xunit.IAsyncLifetime
{
    public Sora.Data.Core.IDataService Data { get; private set; } = default!;
    public IServiceProvider ServiceProvider { get; private set; } = default!;
    public bool SkipTests { get; private set; } = false;
    public string? SkipReason { get; private set; }
    private string _dbFile = default!;

    public async Task InitializeAsync()
    {
        _dbFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sora-sqlite-shared-" + Guid.NewGuid().ToString("n") + ".db");
        var cs = $"Data Source={_dbFile}";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Sora:Data:Sqlite:ConnectionString", cs),
                new KeyValuePair<string, string?>("SORA_DATA_PROVIDER", "sqlite"),
                new KeyValuePair<string, string?>("Sora:AllowMagicInProduction", "true"),
            })
            .Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddSqliteAdapter(o => {
            o.ConnectionString = cs;
            o.DdlPolicy = Sora.Data.Sqlite.SchemaDdlPolicy.AutoCreate;
            o.AllowProductionDdl = true;
        });
        sc.AddSoraDataCore();
        sc.AddSingleton<IDataService, Sora.Data.Core.DataService>();
        ServiceProvider = sc.BuildServiceProvider();
        Data = ServiceProvider.GetRequiredService<IDataService>();
    // rely on adapter DDL policy (AutoCreate) to create the schema during tests
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_dbFile) && System.IO.File.Exists(_dbFile))
        {
            try { System.IO.File.Delete(_dbFile); } catch { }
        }
        await Task.CompletedTask;
    }
}
