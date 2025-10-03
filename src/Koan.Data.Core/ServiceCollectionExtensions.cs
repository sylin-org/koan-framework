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
    // High-level bootstrap: AddKoan()
    public static IServiceCollection AddKoan(this IServiceCollection services)
    {
        services.AddKoanCore();
        var svc = services.AddKoanDataCore();
        // Apply active recipes if Koan.Recipe.Abstractions is referenced
        try
        {
            var ext = Type.GetType("Koan.Recipe.KoanRecipeServiceCollectionExtensions, Koan.Recipe.Abstractions");
            var mi = ext?.GetMethod("ApplyActiveRecipes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            mi?.Invoke(null, new object?[] { services });
        }
        catch { /* optional */ }
        // If Koan.Data.Direct is referenced, auto-register it (no hard reference from Core)
        try
        {
            // Use cached assemblies instead of bespoke AppDomain scanning
            var directReg = AssemblyCache.Instance.GetAllAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.IsSealed && t.IsAbstract == false && t.Name == "DirectRegistration" && t.Namespace == "Koan.Data.Direct");
            var mi = directReg?.GetMethod("AddKoanDataDirect", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            mi?.Invoke(null, new object?[] { services });
        }
        catch { /* optional */ }
        return svc;
    }

    public static IServiceCollection AddKoanDataCore(this IServiceCollection services)
    {
        // Initialize modules (adapters, etc.) that opt-in via IKoanInitializer
        Koan.Core.Hosting.Bootstrap.AppBootstrapper.InitializeModules(services);
        services.TryAddSingleton<Configuration.IDataConnectionResolver, Configuration.DefaultDataConnectionResolver>();
        // Provide a default storage name resolver so naming works even without adapter-specific registration (e.g., JSON adapter)
        services.TryAddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();
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
        return services;
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
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Koan.Secrets.Core");
                var ext = asm?.GetType("Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions");
                var mi = ext?.GetMethod("AddSecretsReferenceConfiguration", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (mi is not null)
                {
                    _ = mi.Invoke(null, new object?[] { cb, null });
                }
            }
            catch { /* optional */ }

            var cfg = cb.Build();
            services.AddSingleton<IConfiguration>(cfg);
        }
        var sp = services.BuildServiceProvider();
        Koan.Core.Hosting.App.AppHost.Current = sp;
        // If secrets configuration is present, upgrade from bootstrap to DI-backed resolver and emit reload
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Koan.Secrets.Core");
            var ext = asm?.GetType("Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions");
            var mi = ext?.GetMethod("UpgradeSecretsConfiguration", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            mi?.Invoke(null, new object?[] { sp });
        }
        catch { /* optional */ }
        try { KoanEnv.TryInitialize(sp); } catch { }
        var rt = sp.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
        rt?.Discover();
        rt?.Start();
        return sp;
    }
}