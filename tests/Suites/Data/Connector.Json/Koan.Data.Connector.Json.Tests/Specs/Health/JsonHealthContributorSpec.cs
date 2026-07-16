using Koan.Core.Observability.Health;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Json.Tests.Specs.Health;

public sealed class JsonHealthContributorSpec
{
    [Fact]
    public async Task Available_but_unelected_json_is_inactive_and_does_not_touch_disk()
    {
        var path = TempPath();
        var configuration = Configuration();
        var registry = Registry(configuration);
        using var services = Services(includeHigherPriorityAdapter: true);
        var contributor = Contributor(services, configuration, registry, path);

        var report = await contributor.Check();

        report.State.Should().Be(HealthState.Unknown);
        contributor.IsCritical.Should().BeFalse();
        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Elected_json_provisions_its_directory_and_reports_ready()
    {
        var path = TempPath();
        var configuration = Configuration();
        var registry = Registry(configuration);
        using var services = Services(includeHigherPriorityAdapter: false);
        var contributor = Contributor(services, configuration, registry, path);

        try
        {
            var report = await contributor.Check();

            report.State.Should().Be(HealthState.Healthy);
            contributor.IsCritical.Should().BeTrue();
            Directory.Exists(path).Should().BeTrue();
            Directory.EnumerateFiles(path, ".__koan-health-*.tmp").Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(path);
        }
    }

    [Fact]
    public async Task Configured_json_source_probes_its_own_directory()
    {
        var root = TempPath();
        var defaultPath = Path.Combine(root, "unused-default");
        var archivePath = Path.Combine(root, "archive");
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Archive:Adapter"] = "json",
            ["Koan:Data:Sources:Archive:json:DirectoryPath"] = archivePath
        });
        var registry = Registry(configuration);
        using var services = Services(includeHigherPriorityAdapter: true);
        var contributor = Contributor(services, configuration, registry, defaultPath);

        try
        {
            var report = await contributor.Check();

            report.State.Should().Be(HealthState.Healthy);
            contributor.IsCritical.Should().BeTrue();
            Directory.Exists(archivePath).Should().BeTrue();
            Directory.Exists(defaultPath).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runtime_json_repository_use_makes_the_provider_participate()
    {
        var path = TempPath();
        var configuration = Configuration();
        var registry = Registry(configuration);
        using var services = Services(includeHigherPriorityAdapter: true);
        var diagnostics = new StubDiagnostics(participations:
        [
            new DataAdapterParticipationInfo("json", "Default")
        ]);
        var contributor = Contributor(services, configuration, registry, path, diagnostics);

        try
        {
            var report = await contributor.Check();

            report.State.Should().Be(HealthState.Healthy);
            contributor.IsCritical.Should().BeTrue();
            Directory.Exists(path).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(path);
        }
    }

    [Fact]
    public async Task Selected_json_fails_loudly_when_its_directory_cannot_be_created()
    {
        var root = TempPath();
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "occupied");
        var configuration = Configuration();
        var registry = Registry(configuration);
        using var services = Services(includeHigherPriorityAdapter: false);
        var contributor = Contributor(services, configuration, registry, filePath);

        try
        {
            var report = await contributor.Check();

            report.State.Should().Be(HealthState.Unhealthy);
            contributor.IsCritical.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static JsonHealthContributor Contributor(
        IServiceProvider services,
        IConfiguration configuration,
        DataSourceRegistry registry,
        string path,
        IDataDiagnostics? diagnostics = null) =>
        new(
            services,
            configuration,
            registry,
            diagnostics ?? new StubDiagnostics(),
            Options.Create(new JsonDataOptions { DirectoryPath = path }));

    private static ServiceProvider Services(bool includeHigherPriorityAdapter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        if (includeHigherPriorityAdapter)
        {
            services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
        }

        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

    private static DataSourceRegistry Registry(IConfiguration configuration)
    {
        var registry = new DataSourceRegistry();
        registry.DiscoverFromConfiguration(configuration);
        return registry;
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"koan-json-health-{Guid.CreateVersion7():N}");

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private sealed class StubDiagnostics(
        IReadOnlyList<EntityConfigInfo>? configs = null,
        IReadOnlyList<DataAdapterParticipationInfo>? participations = null) : IDataDiagnostics
    {
        public IReadOnlyList<EntityConfigInfo> GetEntityConfigsSnapshot() => configs ?? [];
        public IReadOnlyList<DataAdapterParticipationInfo> GetAdapterParticipationsSnapshot() => participations ?? [];
    }

    [ProviderPriority(100)]
    private sealed class SqliteAdapterFactory : IDataAdapterFactory
    {
        public string Provider => "sqlite";

        public bool CanHandle(string provider) =>
            string.Equals(provider, Provider, StringComparison.OrdinalIgnoreCase);

        public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
            IServiceProvider sp,
            string source = "Default")
            where TEntity : class, IEntity<TKey>
            where TKey : notnull =>
            throw new NotSupportedException("The selection-only test factory cannot create repositories.");

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
    }
}
