using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from SqlServerDiscoveryAdapter
        module.AddNote("SQL Server discovery handled by autonomous SqlServerDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new SqlServerOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);

        var maxPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        var connectionValue = string.IsNullOrWhiteSpace(connection.Value)
            ? "auto"
            : connection.Value;
        var connectionIsAuto = string.Equals(connectionValue, "auto", StringComparison.OrdinalIgnoreCase);

        module.AddSetting(
            "ConnectionString",
            connectionIsAuto ? "auto (resolved by discovery)" : connectionValue,
            isSecret: !connectionIsAuto,
            source: connection.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.SqlServer.SqlServerOptionsConfigurator",
                "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
            },
            sourceKey: connection.ResolvedKey);

        module.AddSetting(
            "NamingStyle",
            defaultOptions.NamingStyle.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.NamingStyle);

        module.AddSetting(
            "Separator",
            defaultOptions.Separator,
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.Separator);

        module.AddSetting(
            "EnsureCreatedSupported",
            true.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported);

        module.AddSetting(
            "DefaultPageSize",
            defaultPageSize.Value.ToString(),
            source: defaultPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
            },
            sourceKey: defaultPageSize.ResolvedKey);

        module.AddSetting(
            "MaxPageSize",
            maxPageSize.Value.ToString(),
            source: maxPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.SqlServer.SqlServerAdapterFactory"
            },
            sourceKey: maxPageSize.ResolvedKey);
    }
}

