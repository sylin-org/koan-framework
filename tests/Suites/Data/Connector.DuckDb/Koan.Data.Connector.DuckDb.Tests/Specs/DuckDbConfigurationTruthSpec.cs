using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;
using Koan.Core.Services;
using Koan.Testing.Integration;
using DuckDB.NET.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

[Collection(nameof(DuckDbConfigurationTruthSpec))]
[CollectionDefinition(nameof(DuckDbConfigurationTruthSpec), DisableParallelization = true)]
public sealed class DuckDbConfigurationTruthSpec
{
    [DataAdapter("duckdb")]
    public sealed class ExplicitDuckDbRecord : Entity<ExplicitDuckDbRecord>
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
                             .WithSetting("Koan:Data:DuckDb:ConnectionString", Connection(path))
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
            {
                host.Services.GetRequiredService<IHostEnvironment>()
                    .EnvironmentName.Should().Be(Environments.Production);
                host.Services.GetRequiredService<IOptions<DuckDbOptions>>()
                    .Value.AllowProductionDdl.Should().BeTrue(
                        "DuckDB AutoCreate is the schema decision for the embedded application-owned store");

                var saved = await new ExplicitDuckDbRecord { Value = "first use" }.Save();
                (await ExplicitDuckDbRecord.Get(saved.Id))!.Value.Should().Be("first use");
            }

            await using var connection = new DuckDBConnection(Connection(path));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'main' AND table_name = $name";
            command.Parameters.Add(new DuckDBParameter("name", typeof(ExplicitDuckDbRecord).FullName!));
            Convert.ToInt64(await command.ExecuteScalarAsync()).Should().Be(1);
        }
        finally
        {
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
                             .WithSetting("Koan:Data:DuckDb:ConnectionString", Connection(path))
                             .WithSetting("Koan:Data:DuckDb:DdlPolicy", "NoDdl")
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
            {
                var options = host.Services.GetRequiredService<IOptions<DuckDbOptions>>().Value;
                options.DdlPolicy.Should().Be(RelationalDdlPolicy.NoDdl);
                options.AllowProductionDdl.Should().BeFalse();

                // A refusal has to name the policy that refused and the object it refused, so an operator can
                // act on it. Which layer composed the sentence is not this spec's business.
                var refusal = (await FluentActions.Invoking(() => new ExplicitDuckDbRecord { Value = "rejected" }.Save())
                    .Should().ThrowAsync<InvalidOperationException>()).Which;
                refusal.Message.Should().Contain(nameof(RelationalDdlPolicy.NoDdl))
                    .And.Contain(nameof(ExplicitDuckDbRecord));
            }
            File.Exists(path).Should().BeFalse("NoDdl must fail before DuckDB creates the database");
        }
        finally
        {
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
            .WithSetting("Koan:Data:Sources:Default:duckdb:ConnectionString", Connection(scopedPath))
            .WithSetting("Koan:Data:DuckDb:ConnectionString", Connection(globalPath))
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
            })
            .StartAsync();

        host.Services.GetRequiredService<IOptions<DuckDbOptions>>()
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
            .WithSetting("Koan:Data:Sources:Default:duckdb:ConnectionString", "auto")
            .WithSetting("Koan:Data:DuckDb:ConnectionString", Connection(lowerPath))
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
            })
            .StartAsync();

        host.Services.GetRequiredService<IOptions<DuckDbOptions>>()
            .Value.ConnectionString.Should().Be("Data Source=.koan/data/Koan.duckdb");
        File.Exists(lowerPath).Should().BeFalse();
    }

    [Fact]
    public async Task Foreign_owned_global_default_cannot_bleed_through_options_into_entity_or_direct_routes()
    {
        var duckdbPath = TempDatabase("owned-provider-route");
        var foreignPath = TempDatabase("foreign-global");

        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sources:Default:Adapter", "configuration-test")
                             .WithSetting("Koan:Data:Sources:Default:duckdb:ConnectionString", Connection(duckdbPath))
                             .WithSetting("ConnectionStrings:Default", Connection(foreignPath))
                             .ConfigureServices(services =>
                             {
                                 services.AddKoan();
                                 services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
                             })
                             .StartAsync())
            {
                host.Services.GetRequiredService<IOptions<DuckDbOptions>>()
                    .Value.ConnectionString.Should().Be(Connection(duckdbPath));

                var direct = host.Services.GetRequiredService<IDataService>().Direct(adapter: "duckdb");
                await direct.Execute("CREATE TABLE direct_owned_route (value TEXT NOT NULL)");
                await direct.Execute("INSERT INTO direct_owned_route (value) VALUES ('direct')");

                var saved = await new ExplicitDuckDbRecord { Value = "entity" }.Save();
                (await ExplicitDuckDbRecord.Get(saved.Id))!.Value.Should().Be("entity");
            }

            await using (var connection = new DuckDBConnection($"Data Source={duckdbPath}"))
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
            if (File.Exists(duckdbPath)) File.Delete(duckdbPath);
            if (File.Exists(foreignPath)) File.Delete(foreignPath);
        }
    }

    private static string Connection(string path) => $"Data Source={path}";

    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-duckdb-config-{label}-{Guid.CreateVersion7():n}.db");

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
