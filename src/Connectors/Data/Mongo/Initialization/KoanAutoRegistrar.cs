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
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Mongo.Discovery;
using Koan.Data.Connector.Mongo.Orchestration;

namespace Koan.Data.Connector.Mongo.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static bool _staticSetupComplete;

    public string ModuleName => "Koan.Data.Connector.Mongo";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // CORE-0003: AppDomain-scoped guard for static MongoDB state
        if (!_staticSetupComplete)
        {
            lock (typeof(KoanAutoRegistrar))
            {
                if (!_staticSetupComplete)
                {
                    ConfigureMongoStaticState();
                    _staticSetupComplete = true;
                }
            }
        }

        // ServiceCollection-scoped services (run every time - naturally idempotent via TryAdd)
        services.AddKoanOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.AddSingleton<MongoClientProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAsyncAdapterInitializer, MongoClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdapterReadiness, MongoClientProvider>());
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(INamingDefaultsProvider), typeof(MongoNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, MongoOrchestrationEvaluator>());

        // NEW: Register MongoDB discovery adapter (maintains "Reference = Intent")
        // Adding Koan.Data.Connector.Mongo automatically enables MongoDB discovery capabilities
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MongoDiscoveryAdapter>());
    }

    private static void ConfigureMongoStaticState()
    {
        // Configure MongoDB conventions globally at startup - disables _t discriminators
        var pack = new ConventionPack
        {
            new IgnoreExtraElementsConvention(true),
            new NullBsonValueConvention()
        };

        try
        {
            ConventionRegistry.Register("KoanGlobalConventions", pack, _ => true);
        }
        catch (ArgumentException)
        {
            // Already registered - safe to ignore
        }

        // Disable discriminators by registering custom null discriminator convention
        // This prevents _t/_v fields from being added to documents
        try
        {
            BsonSerializer.RegisterDiscriminatorConvention(
                typeof(object),
                new NoDiscriminatorConvention());
        }
        catch (BsonSerializationException)
        {
            // Already registered - safe to ignore
        }

        try
        {
            BsonSerializer.RegisterSerializationProvider(new JObjectSerializationProvider());
        }
        catch (BsonSerializationException)
        {
            // Already registered - safe to ignore
        }

        // Apply MongoDB GUID optimization directly for v3.5.0 compatibility
        Console.WriteLine("[MONGO-KOAN-AUTO-REGISTRAR] Applying MongoDB GUID optimization directly...");
        var optimizer = new MongoOptimizationAutoRegistrar();
        optimizer.Initialize(null!); // Optimizer doesn't use services parameter
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from MongoDiscoveryAdapter

        var availableProviders = DiscoverAvailableDataProviders();
        report.AddNote($"Available providers: {string.Join(", ", availableProviders)}");
        report.AddNote("MongoDB discovery handled by autonomous MongoDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new MongoOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

        var database = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

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

        report.AddSetting(
            "ConnectionString",
            connectionIsAuto ? "auto (resolved by discovery)" : connectionValue,
            isSecret: !connectionIsAuto,
            source: connection.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Mongo.MongoOptionsConfigurator",
                "Koan.Data.Connector.Mongo.MongoClientProvider",
                "Koan.Data.Connector.Mongo.MongoAdapterFactory"
            },
            sourceKey: connection.ResolvedKey);

        report.AddSetting(
            "Database",
            database.Value ?? defaultOptions.Database,
            source: database.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Mongo.MongoOptionsConfigurator",
                "Koan.Data.Connector.Mongo.MongoClientProvider"
            },
            sourceKey: database.ResolvedKey);

        // Announce schema capability per acceptance criteria
        report.AddSetting(
            Infrastructure.Constants.Bootstrap.EnsureCreatedSupported,
            true.ToString(),
            source: BootSettingSource.Auto,
            consumers: new[]
            {
                "Koan.Data.Connector.Mongo.MongoAdapterFactory"
            },
            sourceKey: Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported);

        // Announce paging guardrails (decision 0044)
        report.AddSetting(
            Infrastructure.Constants.Bootstrap.DefaultPageSize,
            defaultPageSize.Value.ToString(),
            source: defaultPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Mongo.MongoAdapterFactory"
            },
            sourceKey: defaultPageSize.ResolvedKey);

        report.AddSetting(
            Infrastructure.Constants.Bootstrap.MaxPageSize,
            maxPageSize.Value.ToString(),
            source: maxPageSize.Source,
            consumers: new[]
            {
                "Koan.Data.Connector.Mongo.MongoAdapterFactory"
            },
            sourceKey: maxPageSize.ResolvedKey);
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
            if (name.StartsWith("Koan.Data.") && name != "Koan.Data.Connector.Mongo" && name != "Koan.Data.Core")
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



