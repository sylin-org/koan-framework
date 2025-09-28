using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Postgres;

public static class PostgresRegistration
{
    public static IServiceCollection AddPostgresAdapter(this IServiceCollection services, Action<PostgresOptions>? configure = null)
    {
        services.AddKoanOptions<PostgresOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<PostgresOptions>, PostgresOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Koan.Data.Core.Configuration.IDataProviderConnectionFactory, PostgresConnectionFactory>());
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(PostgresNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddRelationalOrchestration();
        return services;
    }
}