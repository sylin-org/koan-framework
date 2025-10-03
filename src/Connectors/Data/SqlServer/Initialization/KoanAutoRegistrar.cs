using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.SqlServer.Discovery;

namespace Koan.Data.Connector.SqlServer.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Connector.SqlServer";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<SqlServerOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<SqlServerOptions>, SqlServerOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqlServerNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqlServerHealthContributor>());

        // Register SQL Server discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.SqlServer automatically enables SQL Server discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, SqlServerDiscoveryAdapter>());

        services.AddSingleton<IDataAdapterFactory, SqlServerAdapterFactory>();
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from SqlServerDiscoveryAdapter
        report.AddNote("SQL Server discovery handled by autonomous SqlServerDiscoveryAdapter");

        // Configure default options for reporting
        var defaultOptions = new SqlServerOptions();

        report.AddSetting("ConnectionString", "auto (resolved by discovery)", isSecret: false);
        report.AddSetting("NamingStyle", defaultOptions.NamingStyle.ToString());
        report.AddSetting("Separator", defaultOptions.Separator);
        report.AddSetting("EnsureCreatedSupported", true.ToString());

        var defSize = Configuration.ReadFirst(cfg, defaultOptions.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        var maxSize = Configuration.ReadFirst(cfg, defaultOptions.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);
        report.AddSetting("DefaultPageSize", defSize.ToString());
        report.AddSetting("MaxPageSize", maxSize.ToString());
    }
}
