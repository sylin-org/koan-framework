using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Postgres;

public static class PostgresRegistration
{
    public static IServiceCollection AddPostgresAdapter(this IServiceCollection services, Action<PostgresOptions>? configure = null)
    {
        services.AddKoanOptions<PostgresOptions, PostgresOptionsConfigurator>(
            Infrastructure.Constants.Configuration.Keys.Section,
            configuratorLifetime: ServiceLifetime.Singleton);
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, PostgresConnectionFactory>());
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddRelationalOrchestration();
        return services;
    }
}
