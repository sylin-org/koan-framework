using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Logging;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Koan.Data.Connector.Sqlite";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));
        services.AddKoanOptions<SqliteOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqliteNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());

        // Register SQLite discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Sqlite automatically enables SQLite discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, Discovery.SqliteDiscoveryAdapter>());

        // Ensure relational orchestration services are available (schema validation/creation)
        services.AddRelationalOrchestration();
        // Bridge SQLite governance options into relational orchestrator options
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqliteToRelationalBridgeConfigurator>());

        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from SqliteDiscoveryAdapter
        report.AddNote("SQLite discovery handled by autonomous SqliteDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new SqliteOptions();
        var defaultPageSize = Koan.Core.Configuration.Read(cfg,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize, defaultOptions.DefaultPageSize);
        var maxPageSize = Koan.Core.Configuration.Read(cfg,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize, defaultOptions.MaxPageSize);

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("NamingStyle", defaultOptions.NamingStyle.ToString());
        report.AddSetting("Separator", defaultOptions.Separator);
        report.AddSetting(Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.DefaultPageSize, defaultPageSize.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.MaxPageSize, maxPageSize.ToString());
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}

