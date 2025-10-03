using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite;

public static class SqliteRegistration
{
    public static IServiceCollection AddSqliteAdapter(this IServiceCollection services, Action<SqliteOptions>? configure = null)
    {
        services.AddKoanOptions<SqliteOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<SqliteOptions>, SqliteOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        services.AddRelationalOrchestration();
        // Register bridge AFTER orchestrator so options pipeline is complete and test delegates win
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<RelationalMaterializationOptions>, SqliteToRelationalBridgeConfigurator>());
        // Ensure health contributor is available even outside Koan bootstrap
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, SqliteHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, SqliteAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, SqliteConnectionFactory>());
        // Provide naming defaults so relational naming resolves to provider's convention
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(SqliteNamingDefaultsProvider), ServiceLifetime.Singleton));
        return services;
    }
}
