using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
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
using MongoItems = Koan.Data.Connector.Mongo.Infrastructure.MongoProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        // Autonomous discovery adapter handles all connection string resolution
        // Boot report shows discovery results from MongoDiscoveryAdapter

        var availableProviders = DiscoverAvailableDataProviders();
        module.AddNote($"Available providers: {string.Join(", ", availableProviders)}");
        module.AddNote("MongoDB discovery handled by autonomous MongoDiscoveryAdapter");

        // Configure default options for reporting with provenance metadata
        var defaultOptions = new MongoOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            MongoItems.ConnectionStringKeys);

        var database = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Database,
            MongoItems.DatabaseKeys);

        var defaultPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.DefaultPageSize,
            MongoItems.DefaultPageSizeKeys);

        var maxPageSize = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.MaxPageSize,
            MongoItems.MaxPageSizeKeys);

        var username = Configuration.ReadFirstWithSource(
            cfg,
            string.Empty,
            "Koan:Data:Mongo:Username",
            "Koan:Data:Username");

        var password = Configuration.ReadFirstWithSource(
            cfg,
            string.Empty,
            "Koan:Data:Mongo:Password",
            "Koan:Data:Password");

        var effectiveConnectionString = ResolveConnectionStringForReporting(
            cfg,
            defaultOptions,
            connection,
            database.Value ?? defaultOptions.Database,
            username.Value,
            password.Value);

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSourceKey = connection.ResolvedKey ?? MongoItems.ConnectionString.Key;
        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        Publish(
            module,
            MongoItems.ConnectionString,
            connection,
            displayOverride: effectiveConnectionString,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        Publish(module, MongoItems.Database, database, database.Value ?? defaultOptions.Database);

        module.AddSetting(
            MongoItems.EnsureCreatedSupported,
            ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true),
            true,
            usedDefault: true,
            sanitizeOverride: false);

        Publish(module, MongoItems.DefaultPageSize, defaultPageSize);

        Publish(module, MongoItems.MaxPageSize, maxPageSize);
    }

    private static void Publish<T>(Koan.Core.Provenance.ProvenanceModuleWriter module, ProvenanceItem item, Koan.Core.ConfigurationValue<T> value, object? displayOverride = null, ProvenancePublicationMode? modeOverride = null, bool? usedDefaultOverride = null, string? sourceKeyOverride = null, bool? sanitizeOverride = null)
    {
        module.AddSetting(
            item,
            modeOverride ?? ProvenanceModes.FromConfigurationValue(value),
            displayOverride ?? value.Value,
            sourceKey: sourceKeyOverride ?? value.ResolvedKey,
            usedDefault: usedDefaultOverride ?? value.UsedDefault,
            sanitizeOverride: sanitizeOverride);
    }

    private static string ResolveConnectionStringForReporting(
        IConfiguration? configuration,
        MongoOptions defaults,
    Koan.Core.ConfigurationValue<string> configuredConnection,
        string? database,
        string? username,
        string? password)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnection.Value) &&
            !string.Equals(configuredConnection.Value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return configuredConnection.Value!;
        }

        var resolvedDatabase = string.IsNullOrWhiteSpace(database) ? defaults.Database : database!;
        var safeConfiguration = configuration ?? new ConfigurationBuilder().AddInMemoryCollection().Build();

        try
        {
            var adapter = new MongoDiscoveryAdapter(safeConfiguration, NullLogger<MongoDiscoveryAdapter>.Instance);

            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(resolvedDatabase))
            {
                parameters["database"] = resolvedDatabase;
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                parameters["username"] = username!;
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                parameters["password"] = password!;
            }

            var context = new DiscoveryContext
            {
                OrchestrationMode = KoanEnv.OrchestrationMode,
                Configuration = safeConfiguration,
                HealthCheckTimeout = TimeSpan.FromMilliseconds(500),
                Parameters = parameters.Count > 0 ? parameters : null
            };

            var result = adapter.DiscoverAsync(context).GetAwaiter().GetResult();
            if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl))
            {
                return result.ServiceUrl!;
            }
        }
        catch
        {
            // Discovery failures fall back to defaults below.
        }

        return BuildFallbackConnectionString(defaults, resolvedDatabase, username, password);
    }

    private static string BuildFallbackConnectionString(
        MongoOptions defaults,
        string? database,
        string? username,
        string? password)
    {
        var fallback = defaults.ConnectionString;
        if (!string.IsNullOrWhiteSpace(fallback) &&
            !string.Equals(fallback, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        var auth = string.IsNullOrWhiteSpace(username)
            ? string.Empty
            : $"{username}:{password ?? string.Empty}@";

        var databaseSegment = string.IsNullOrWhiteSpace(database)
            ? defaults.Database
            : database!;

        return $"mongodb://{auth}localhost:27017/{databaseSegment}";
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




