using Koan.Core;
using Koan.Core.Observability.Health;
using Koan.Data.Abstractions.Naming;
using Koan.Core.Services;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>SQLite readiness follows runtime participation and probes the same routed source as Entity operations.</summary>
public sealed class SqliteHealthContributorSpec
{
    public sealed class HealthRecord : Entity<HealthRecord>
    {
        public string Value { get; set; } = "";
    }

    [DataAdapter("sqlite")]
    public sealed class ExplicitSqliteRecord : Entity<ExplicitSqliteRecord>
    {
        public string Value { get; set; } = "";
    }

    [Fact]
    public async Task Available_but_unelected_sqlite_is_non_critical_and_does_not_touch_disk()
    {
        var databasePath = TempDatabase("inactive");
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(databasePath))
            .ConfigureServices(services =>
            {
                services.AddKoan();
                services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
            })
            .StartAsync();

        var contributor = SqliteHealth(host.Services);
        var report = await contributor.Check();

        contributor.IsCritical.Should().BeFalse();
        report.State.Should().Be(HealthState.Unknown);
        File.Exists(databasePath).Should().BeFalse();
    }

    [Fact]
    public async Task Runtime_repository_use_activates_sqlite_but_entity_description_does_not()
    {
        var databasePath = TempDatabase("runtime-participation");
        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(databasePath))
                             .ConfigureServices(services =>
                             {
                                 services.AddKoan();
                                 services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
                             })
                             .StartAsync())
            {
                var contributor = SqliteHealth(host.Services);

                _ = AggregateConfigs.Get<ExplicitSqliteRecord, string>(host.Services);
                var describedOnly = await contributor.Check();
                describedOnly.State.Should().Be(HealthState.Unknown);
                contributor.IsCritical.Should().BeFalse();
                File.Exists(databasePath).Should().BeFalse();

                var saved = await new ExplicitSqliteRecord { Value = "selected-at-runtime" }.Save();
                (await ExplicitSqliteRecord.Get(saved.Id))!.Value.Should().Be("selected-at-runtime");

                var active = await contributor.Check();
                contributor.IsCritical.Should().BeTrue();
                active.State.Should().Be(HealthState.Healthy);
                File.Exists(databasePath).Should().BeTrue();
            }

            File.Delete(databasePath);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Direct_execution_activates_sqlite_but_session_description_does_not()
    {
        var databasePath = TempDatabase("direct-participation");
        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(databasePath))
                             .ConfigureServices(services =>
                             {
                                 services.AddKoan();
                                 services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
                             })
                             .StartAsync())
            {
                var contributor = SqliteHealth(host.Services);
                var direct = host.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");

                (await contributor.Check()).State.Should().Be(HealthState.Unknown);
                contributor.IsCritical.Should().BeFalse();
                File.Exists(databasePath).Should().BeFalse();

                await direct.Execute("CREATE TABLE direct_participation_probe (value TEXT NOT NULL)");

                (await contributor.Check()).State.Should().Be(HealthState.Healthy);
                contributor.IsCritical.Should().BeTrue();
                File.Exists(databasePath).Should().BeTrue();
            }

            File.Delete(databasePath);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Elected_sqlite_health_probes_the_authoritative_default_source()
    {
        var databasePath = TempDatabase("active");
        var fallbackPath = TempDatabase("unused-fallback");
        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                             .WithSetting("Koan:Data:Sources:Default:ConnectionString", Connection(databasePath))
                             .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(fallbackPath))
                             .ConfigureServices(services => services.AddKoan())
                             .StartAsync())
            {
                var contributor = SqliteHealth(host.Services);
                var report = await contributor.Check();

                contributor.IsCritical.Should().BeTrue();
                report.State.Should().Be(HealthState.Healthy);
                File.Exists(databasePath).Should().BeTrue();
                File.Exists(fallbackPath).Should().BeFalse(
                    "health must inspect the same configured Default source used by repositories");

                var saved = await new HealthRecord { Value = "same-target" }.Save();
                (await HealthRecord.Get(saved.Id))!.Value.Should().Be("same-target");
                File.Exists(fallbackPath).Should().BeFalse(
                    "repository operations and readiness must agree on the authoritative Default source");

            }

            File.Delete(databasePath);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
            if (File.Exists(fallbackPath)) File.Delete(fallbackPath);
        }
    }

    [Fact]
    public async Task Configured_named_sqlite_source_stays_inactive_until_selected_then_probes_its_own_database()
    {
        var databasePath = TempDatabase("archive");
        var fallbackPath = TempDatabase("named-unused-fallback");
        try
        {
            await using (var host = await KoanIntegrationHost.Configure()
                             .WithSetting("Koan:Environment", "Test")
                             .WithSetting("Koan:Data:Sources:Archive:Adapter", "sqlite")
                             .WithSetting("Koan:Data:Sources:Archive:ConnectionString", Connection(databasePath))
                             .WithSetting("Koan:Data:Sqlite:ConnectionString", Connection(fallbackPath))
                             .ConfigureServices(services =>
                             {
                                 services.AddKoan();
                                 services.AddSingleton<IDataAdapterFactory, HigherPriorityAdapter>();
                             })
                             .StartAsync())
            {
                var contributor = SqliteHealth(host.Services);
                var available = await contributor.Check();

                contributor.IsCritical.Should().BeFalse();
                available.State.Should().Be(HealthState.Unknown);
                File.Exists(databasePath).Should().BeFalse();
                File.Exists(fallbackPath).Should().BeFalse();

                using (EntityContext.Source("Archive"))
                {
                    await new HealthRecord { Value = "selected" }.Save();
                }

                var active = await contributor.Check();

                contributor.IsCritical.Should().BeTrue();
                active.State.Should().Be(HealthState.Healthy);
                File.Exists(databasePath).Should().BeTrue();
                File.Exists(fallbackPath).Should().BeFalse();
            }

            File.Delete(databasePath);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
            if (File.Exists(fallbackPath)) File.Delete(fallbackPath);
        }
    }

    [Fact]
    public async Task Selected_sqlite_reports_unhealthy_when_its_database_directory_is_a_file()
    {
        var occupiedPath = TempDatabase("occupied-parent");
        await File.WriteAllTextAsync(occupiedPath, "not-a-directory");
        var databasePath = Path.Combine(occupiedPath, "health.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                .WithSetting("Koan:Data:Sources:Default:ConnectionString", Connection(databasePath))
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();

            var contributor = SqliteHealth(host.Services);
            var report = await contributor.Check();

            contributor.IsCritical.Should().BeTrue();
            report.State.Should().Be(HealthState.Unhealthy);
            report.Description.Should().Be("Data source 'Default' is unavailable");
            report.Data.Should().ContainKey("failedSource").WhoseValue.Should().Be("Default");
        }
        finally
        {
            if (File.Exists(occupiedPath)) File.Delete(occupiedPath);
        }
    }

    [Fact]
    public async Task Readiness_probes_every_active_source_and_attributes_the_failure()
    {
        var healthyPath = TempDatabase("multi-source-healthy");
        var occupiedPath = TempDatabase("multi-source-occupied-parent");
        await File.WriteAllTextAsync(occupiedPath, "not-a-directory");
        var brokenPath = Path.Combine(occupiedPath, "health.db");

        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                .WithSetting("Koan:Data:Sources:Default:ConnectionString", Connection(healthyPath))
                .WithSetting("Koan:Data:Sources:ZArchive:Adapter", "sqlite")
                .WithSetting("Koan:Data:Sources:ZArchive:ConnectionString", Connection(brokenPath))
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();

            using (EntityContext.Source("ZArchive"))
            {
                _ = host.Services
                    .GetRequiredService<IDataService>()
                    .GetRepository<HealthRecord, string>();
            }

            var report = await SqliteHealth(host.Services).Check();

            report.State.Should().Be(HealthState.Unhealthy);
            report.Data.Should().ContainKey("failedSource").WhoseValue.Should().Be("ZArchive");
            File.Exists(healthyPath).Should().BeTrue(
                "the healthy source sorts first and must be probed before the later failing source");
        }
        finally
        {
            if (File.Exists(healthyPath)) File.Delete(healthyPath);
            if (File.Exists(occupiedPath)) File.Delete(occupiedPath);
        }
    }

    private static IHealthContributor SqliteHealth(IServiceProvider services)
        => services.GetServices<IHealthContributor>().Single(contributor => contributor.Name == "data:sqlite");

    private static string Connection(string path) => $"Data Source={path};Pooling=True";
    private static string TempDatabase(string label)
        => Path.Combine(Path.GetTempPath(), $"koan-sqlite-health-{label}-{Guid.CreateVersion7():n}.db");

    [ProviderPriority(100)]
    private sealed class HigherPriorityAdapter : IDataAdapterFactory
    {
        public string Provider => "health-test";
        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
            => throw new NotSupportedException("The selection-only health adapter does not create repositories.");

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
    }
}
