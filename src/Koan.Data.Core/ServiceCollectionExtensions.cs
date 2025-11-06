using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Core.Schema;

namespace Koan.Data.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanDataCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Initialize modules (adapters, etc.) that opt-in via IKoanInitializer
        AppBootstrapper.InitializeModules(services);
        RegisterKoanDataCoreServices(services);
        return services;
    }

    internal static void RegisterKoanDataCoreServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(d => d.ServiceType == typeof(IDataService)))
        {
            return;
        }

        services.TryAddSingleton<Configuration.IDataConnectionResolver, Configuration.DefaultDataConnectionResolver>();
        // Provide a default storage name resolver so naming works even without adapter-specific registration (e.g., JSON adapter)
        services.TryAddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        // Note: Partition context provided by EntityContext (static, no DI needed) - see DATA-0077
        services.AddKoanOptions<Options.DirectOptions>(Infrastructure.Constants.Configuration.Direct.Section);
        // Vector defaults now live in Koan.Data.Vector; apps should call AddKoanDataVector() to enable vector features.
        services.AddKoanOptions<DataRuntimeOptions>();
        services.AddSingleton<IAggregateIdentityManager, AggregateIdentityManager>();

        // Data source registry for source/adapter routing (DATA-0077)
        services.AddSingleton<DataSourceRegistry>(sp =>
        {
            var registry = new DataSourceRegistry();
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<DataSourceRegistry>>();
            registry.DiscoverFromConfiguration(config, logger);
            return registry;
        });

        services.AddSingleton<IDataService, DataService>();
        services.TryAddSingleton(typeof(EntitySchemaGuard<,>));
        services.TryAddSingleton(typeof(ISchemaHealthContributor<,>), typeof(AggregateSchemaHealthContributor<,>));
        services.AddSingleton<IDataDiagnostics, DataDiagnostics>();
        // Decorate repositories registered as IDataRepository<,>
        services.TryDecorate(typeof(IDataRepository<,>), typeof(RepositoryFacade<,>));
        // Relationship metadata scanning (ParentAttribute, etc.)
        services.TryAddSingleton<Koan.Data.Core.Relationships.IRelationshipMetadata, Koan.Data.Core.Relationships.RelationshipMetadataService>();
        Koan.Data.Core.Model.EntityMetadataProvider.RelationshipMetadataAccessor = sp => sp.GetRequiredService<Koan.Data.Core.Relationships.IRelationshipMetadata>();
    }

    // One-liner startup: builds provider, runs discovery, starts runtime (greenfield)
    public static IServiceProvider StartKoan(this IServiceCollection services)
    {
        // Avoid duplicate registration if already configured
        if (!services.Any(d => d.ServiceType == typeof(Koan.Core.Hosting.Runtime.IAppRuntime)))
            services.AddKoan();

        // Provide a default IConfiguration only if the host hasn't already registered one
        if (!services.Any(d => d.ServiceType == typeof(IConfiguration)))
        {
            var cb = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();
            // If Koan.Secrets.Core is referenced, auto-add the secrets configuration wrapper
            TryInvokeSecretsBootstrap("AddSecretsReferenceConfiguration", cb, null);

            var cfg = cb.Build();
            services.AddSingleton<IConfiguration>(cfg);
        }
        var sp = services.BuildServiceProvider();
        Koan.Core.Hosting.App.AppHost.Current = sp;
        // If secrets configuration is present, upgrade from bootstrap to DI-backed resolver and emit reload
        TryInvokeSecretsBootstrap("UpgradeSecretsConfiguration", sp);
        try { KoanEnv.TryInitialize(sp); } catch { }
        var rt = sp.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
        rt?.Discover();
        rt?.Start();
        return sp;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, "Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions", "Koan.Secrets.Core")]
    private static void TryInvokeSecretsBootstrap(string methodName, params object?[]? args)
    {
        try
        {
            var secretsType = Type.GetType("Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions, Koan.Secrets.Core", throwOnError: false, ignoreCase: false);
            var method = secretsType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, args ?? Array.Empty<object?>());
        }
        catch { /* optional */ }
    }
}