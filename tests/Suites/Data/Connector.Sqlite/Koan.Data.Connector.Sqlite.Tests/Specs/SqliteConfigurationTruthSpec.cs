using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;
using Koan.Core.Services;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

[Collection(nameof(SqliteConfigurationTruthSpec))]
[CollectionDefinition(nameof(SqliteConfigurationTruthSpec), DisableParallelization = true)]
public sealed class SqliteConfigurationTruthSpec
{
    [DataAdapter("sqlite")]
    public sealed class ExplicitSqliteRecord : Entity<ExplicitSqliteRecord>
    {
        public string Value { get; set; } = "";
    }

    [Fact]
    public async Task Production_host_keeps_zero_configuration_autocreate_literal()
    {
        var path = TempDatabase("production-autocreate");

        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithEnvironment(Environments.Production)
                             .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(path))
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
            {
                host.Services.GetRequiredService<IHostEnvironment>()
                    .EnvironmentName.Should().Be(Environments.Production);
                host.Services.GetRequiredService<IOptions<SqliteOptions>>()
                    .Value.AllowProductionDdl.Should().BeTrue(
                        "SQLite AutoCreate is the schema decision for the embedded application-owned store");

                var saved = await new ExplicitSqliteRecord { Value = "first use" }.Save();
                (await ExplicitSqliteRecord.Get(saved.Id))!.Value.Should().Be("first use");
            }

            await using var connection = new SqliteConnection(Connection(path));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            command.Parameters.AddWithValue("$name", typeof(ExplicitSqliteRecord).FullName!);
            Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task NoDdl_remains_an_explicit_non_creating_policy()
    {
        var path = TempDatabase("production-no-ddl");

        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithEnvironment(Environments.Production)
                             .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(path))
                             .WithSetting("Koan:Data:Sqlite:DdlPolicy", "NoDdl")
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
            {
                var options = host.Services.GetRequiredService<IOptions<SqliteOptions>>().Value;
                options.DdlPolicy.Should().Be(RelationalDdlPolicy.NoDdl);
                options.AllowProductionDdl.Should().BeFalse();

                // A refusal has to name the policy that refused and the object it refused, so an operator can
                // act on it. Which layer composed the sentence is not this spec's business.
                var refusal = (await FluentActions.Invoking(() => new ExplicitSqliteRecord { Value = "rejected" }.Save())
                    .Should().ThrowAsync<InvalidOperationException>()).Which;
                refusal.Message.Should().Contain(nameof(RelationalDdlPolicy.NoDdl))
                    .And.Contain(nameof(ExplicitSqliteRecord));
            }
            File.Exists(path).Should().BeFalse("NoDdl must fail before SQLite creates the database");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Provider_scoped_default_precedes_provider_global_fallback_without_touching_disk()
    {
        var scopedPath = TempDatabase("provider-scoped");
        var globalPath = TempDatabase("provider-global");

        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:sqlite:ConnectionString", Connection(scopedPath))
            .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(globalPath))
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
            })
            .StartAsync();

        host.Services.GetRequiredService<IOptions<SqliteOptions>>()
            .Value.ConnectionString.Should().Be(Connection(scopedPath));
        File.Exists(scopedPath).Should().BeFalse("configuration resolution is not a storage operation");
        File.Exists(globalPath).Should().BeFalse();
    }

    [Fact]
    public async Task Provider_scoped_auto_resolves_to_the_deterministic_embedded_default_without_io()
    {
        var lowerPath = TempDatabase("ignored-lower");

        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:sqlite:ConnectionString", "auto")
            .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(lowerPath))
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
            })
            .StartAsync();

        host.Services.GetRequiredService<IOptions<SqliteOptions>>()
            .Value.ConnectionString.Should().Be("Data Source=.koan/data/Koan.sqlite");
        File.Exists(lowerPath).Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_owned_global_default_cannot_bleed_through_options_into_entity_or_direct_routes()
    {
        var sqlitePath = TempDatabase("owned-provider-route");
        var foreignPath = TempDatabase("foreign-global");

        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sources:Default:Adapter", "configuration-test")
                             .WithSetting("Koan:Data:Sources:Default:sqlite:ConnectionString", Connection(sqlitePath))
                             .WithSetting("ConnectionStrings:Default", Connection(foreignPath))
                             .ConfigureServices(services =>
                             {
                                 services.AddKoan();
                                 services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
                             })
                             .StartAsync())
            {
                host.Services.GetRequiredService<IOptions<SqliteOptions>>()
                    .Value.ConnectionString.Should().Be(Connection(sqlitePath));

                var direct = host.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");
                await direct.Execute("CREATE TABLE direct_owned_route (value TEXT NOT NULL)");
                await direct.Execute("INSERT INTO direct_owned_route (value) VALUES ('direct')");

                var saved = await new ExplicitSqliteRecord { Value = "entity" }.Save();
                (await ExplicitSqliteRecord.Get(saved.Id))!.Value.Should().Be("entity");
            }

            await using (var connection = new SqliteConnection($"Data Source={sqlitePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM direct_owned_route";
                Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
            }
            File.Exists(foreignPath).Should().BeFalse(
                "a global Default connection belongs to its configured source adapter, not every referenced provider");
        }
        finally
        {
            if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
            if (File.Exists(foreignPath)) File.Delete(foreignPath);
        }
    }

    private static string Connection(string path) => $"Data Source={path};Pooling=True";

    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-sqlite-config-{label}-{Guid.CreateVersion7():n}.db");

    [ProviderPriority(100)]
    private sealed class HigherPriorityAdapter : IDataAdapterFactory
    {
        public string Provider => "configuration-test";

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider sp,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => throw new NotSupportedException("The selection-only adapter does not create repositories.");

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
    }
}
