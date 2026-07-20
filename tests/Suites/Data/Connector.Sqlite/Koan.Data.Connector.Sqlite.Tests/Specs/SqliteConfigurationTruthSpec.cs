using Koan.Core;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
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

                await FluentActions.Invoking(() => new ExplicitSqliteRecord { Value = "rejected" }.Save())
                    .Should().ThrowAsync<SqliteException>();
            }

            await using var connection = new SqliteConnection(Connection(path));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
            command.Parameters.AddWithValue("$name", typeof(ExplicitSqliteRecord).FullName!);
            Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(0);
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
    public async Task Provider_scoped_auto_delegates_to_pure_discovery_instead_of_lower_configuration()
    {
        var discoveredPath = TempDatabase("discovered");
        var lowerPath = TempDatabase("ignored-lower");
        var discovery = new CapturingDiscoveryCoordinator(Connection(discoveredPath));
        File.Exists(discoveredPath).Should().BeFalse();

        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:sqlite:ConnectionString", "auto")
            .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(lowerPath))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IServiceDiscoveryCoordinator>(discovery);
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
            })
            .StartAsync();

        File.Exists(discoveredPath).Should().BeFalse("host composition must not materialize a discovery candidate");

        host.Services.GetRequiredService<IOptions<SqliteOptions>>()
            .Value.ConnectionString.Should().Be(Connection(discoveredPath));
        discovery.Context.Should().NotBeNull();
        discovery.Context!.RequireHealthValidation.Should().BeFalse(
            "target selection must not create or probe storage before the adapter participates");
        File.Exists(discoveredPath).Should().BeFalse();
        File.Exists(lowerPath).Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_owned_global_default_cannot_bleed_through_options_into_entity_or_direct_routes()
    {
        var discoveredPath = TempDatabase("owned-discovery");
        var foreignPath = TempDatabase("foreign-global");
        var discovery = new CapturingDiscoveryCoordinator(Connection(discoveredPath));

        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sources:Default:Adapter", "configuration-test")
                             .WithSetting("ConnectionStrings:Default", Connection(foreignPath))
                             .ConfigureServices(services =>
                             {
                                 services.AddSingleton<IServiceDiscoveryCoordinator>(discovery);
                                 services.AddKoan();
                                 services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
                             })
                             .StartAsync())
            {
                host.Services.GetRequiredService<IOptions<SqliteOptions>>()
                    .Value.ConnectionString.Should().Be(Connection(discoveredPath));

                var direct = host.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");
                await direct.Execute("CREATE TABLE direct_owned_route (value TEXT NOT NULL)");
                await direct.Execute("INSERT INTO direct_owned_route (value) VALUES ('direct')");

                var saved = await new ExplicitSqliteRecord { Value = "entity" }.Save();
                (await ExplicitSqliteRecord.Get(saved.Id))!.Value.Should().Be("entity");
            }

            await using (var connection = new SqliteConnection($"Data Source={discoveredPath};Pooling=False"))
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
            if (File.Exists(discoveredPath)) File.Delete(discoveredPath);
            if (File.Exists(foreignPath)) File.Delete(foreignPath);
        }
    }

    private static string Connection(string path) => $"Data Source={path};Pooling=True";

    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-sqlite-config-{label}-{Guid.CreateVersion7():n}.db");

    private sealed class CapturingDiscoveryCoordinator(string connectionString) : IServiceDiscoveryCoordinator
    {
        public DiscoveryContext? Context { get; private set; }

        public Task<AdapterDiscoveryResult> DiscoverService(
            string serviceName,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(AdapterDiscoveryResult.Success(
                serviceName,
                connectionString,
                "test-discovery"));
        }

        public Task<AdapterDiscoveryResult> ResolveServiceIntent(
            string serviceName,
            string intent,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
            => DiscoverService(serviceName, context, cancellationToken);

        public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => [];
    }

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
