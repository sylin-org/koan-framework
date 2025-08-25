using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Relational.Orchestration;

namespace Sora.Data.Postgres;

public static class PostgresRegistration
{
    public static IServiceCollection AddPostgresAdapter(this IServiceCollection services, Action<PostgresOptions>? configure = null)
    {
    services.AddSoraOptions<PostgresOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<PostgresOptions>, PostgresOptionsConfigurator>());
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, PostgresHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Sora.Data.Core.Configuration.IDataProviderConnectionFactory, PostgresConnectionFactory>());
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(PostgresNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddRelationalOrchestration();
        return services;
    }
}