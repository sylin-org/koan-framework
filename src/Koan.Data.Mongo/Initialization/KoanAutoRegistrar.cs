using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Data.Abstractions;
using Koan.Data.Mongo.Orchestration;

namespace Koan.Data.Mongo.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Mongo";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Mongo.Initialization.KoanAutoRegistrar");
        logger?.Log(LogLevel.Debug, "Koan.Data.Mongo KoanAutoRegistrar loaded.");

        // Configure MongoDB conventions globally at startup - disables _t discriminators
        var pack = new ConventionPack
        {
            new IgnoreExtraElementsConvention(true),
            new NullBsonValueConvention()
        };
        ConventionRegistry.Register("KoanGlobalConventions", pack, _ => true);

        // Disable discriminators by registering custom null discriminator convention
        // This prevents _t/_v fields from being added to documents
        BsonSerializer.RegisterDiscriminatorConvention(
            typeof(object),
            new NoDiscriminatorConvention());

        BsonSerializer.RegisterSerializationProvider(new JObjectSerializationProvider());

        // Note: Optimized serialization will be registered lazily when repositories are created
        // Note: Conventions handle null values, no custom serializer needed

        services.AddKoanOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.AddSingleton<MongoClientProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAsyncAdapterInitializer, MongoClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdapterReadiness, MongoClientProvider>());
        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Abstractions.Naming.INamingDefaultsProvider), typeof(MongoNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, MongoOrchestrationEvaluator>());

        // Apply MongoDB GUID optimization directly for v3.5.0 compatibility
        Console.WriteLine("[MONGO-KOAN-AUTO-REGISTRAR] Applying MongoDB GUID optimization directly...");
        var optimizer = new MongoOptimizationAutoRegistrar();
        optimizer.Initialize(services);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // NEW: Decision logging for connection string resolution
        var connectionAttempts = new List<(string source, string connectionString, bool canConnect, string? error)>();

        // Try configured connection strings first
        var configuredCs = Configuration.ReadFirst(cfg, string.Empty,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        if (!string.IsNullOrWhiteSpace(configuredCs))
        {
            report.AddDiscovery("configuration", configuredCs);
            connectionAttempts.Add(("configuration", configuredCs, true, null)); // Assume configured strings work
        }

        // Auto-discovery logic with decision logging
        var finalCs = configuredCs;
        if (string.IsNullOrWhiteSpace(finalCs))
        {
            var isProd = KoanEnv.IsProduction;
            var inContainer = KoanEnv.InContainer;

            if (isProd)
            {
                finalCs = MongoConstants.DefaultLocalUri;
                report.AddDiscovery("production-default", finalCs);
                connectionAttempts.Add(("production-default", finalCs, true, null));
            }
            else
            {
                if (inContainer)
                {
                    finalCs = MongoConstants.DefaultComposeUri;
                    report.AddDiscovery("container-discovery", finalCs);
                    connectionAttempts.Add(("container-discovery", finalCs, true, null));
                }
                else
                {
                    finalCs = MongoConstants.DefaultLocalUri;
                    report.AddDiscovery("localhost-fallback", finalCs);
                    connectionAttempts.Add(("localhost-fallback", finalCs, true, null));
                }
            }
        }

        // Normalize connection string
        if (!string.IsNullOrWhiteSpace(finalCs) &&
            !finalCs.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) &&
            !finalCs.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            finalCs = "mongodb://" + finalCs.Trim();
        }

        // Log connection attempts
        foreach (var attempt in connectionAttempts)
        {
            report.AddConnectionAttempt("Data.Mongo", attempt.connectionString, attempt.canConnect, attempt.error);
        }

        // Log provider election decision
        var availableProviders = DiscoverAvailableDataProviders();
        if (connectionAttempts.Any(a => a.canConnect))
        {
            report.AddProviderElection("Data", "Mongo", availableProviders, "first successful connection");
        }
        else
        {
            report.AddDecision("Data", "InMemory", "no Mongo connection available", availableProviders);
        }

        var o = new MongoOptions
        {
            ConnectionString = finalCs,
            Database = Configuration.ReadFirst(cfg, "Koan",
                Infrastructure.Constants.Configuration.Keys.Database,
                Infrastructure.Constants.Configuration.Keys.AltDatabase)
        };

        report.AddSetting("Database", o.Database);
        report.AddSetting("ConnectionString", finalCs, isSecret: true);
        // Announce schema capability per acceptance criteria
        report.AddSetting(Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        // Announce paging guardrails (decision 0044)
        var defSize = Configuration.ReadFirst(cfg, o.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        var maxSize = Configuration.ReadFirst(cfg, o.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);
        report.AddSetting(Infrastructure.Constants.Bootstrap.DefaultPageSize, defSize.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.MaxPageSize, maxSize.ToString());
    }

    private static string[] DiscoverAvailableDataProviders()
    {
        // Scan cached assemblies for other data providers
        var providers = new List<string> { "Mongo" }; // Always include self

        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("Koan.Data.") && name != "Koan.Data.Mongo" && name != "Koan.Data.Core")
            {
                var providerName = name.Substring("Koan.Data.".Length);
                providers.Add(providerName);
            }
        }

        return providers.ToArray();
    }
}

/// <summary>
/// Custom discriminator convention that disables discriminator serialization entirely.
/// This prevents MongoDB from adding _t fields to documents.
/// </summary>
public class NoDiscriminatorConvention : IDiscriminatorConvention
{
    public string ElementName => "_t";
    public Type GetActualType(MongoDB.Bson.IO.IBsonReader bsonReader, Type nominalType) => nominalType;
    public MongoDB.Bson.BsonValue GetDiscriminator(Type nominalType, Type actualType) => null!;
}

/// <summary>
/// Convention to handle nulls for BsonValue properties globally.
/// </summary>
public class NullBsonValueConvention : IMemberMapConvention
{
    public string Name => "NullBsonValueConvention";
    public void Apply(BsonMemberMap memberMap)
    {
        if (memberMap.MemberType == typeof(BsonValue))
        {
            memberMap.SetDefaultValue(BsonNull.Value);
        }
    }
}


