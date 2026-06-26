using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Reporting;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Mongo.Discovery;
using Koan.Data.Connector.Mongo.Orchestration;
using Koan.ZenGarden.Core;
using MongoItems = Koan.Data.Connector.Mongo.Infrastructure.MongoProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Mongo.Initialization;

/// <summary>
/// The MongoDB connector boot module (ARCH-0086 / ARCH-0103 §L). Folds the former split between the
/// hand-written <c>KoanAutoRegistrar</c> (DI + boot report, with its in-line static BSON config and a
/// bespoke connection re-discovery) and the independently-discovered <c>MongoOptimizationAutoRegistrar</c>
/// into ONE <see cref="KoanModule"/>: <see cref="Register"/> wires the services and applies the
/// once-guarded driver configuration (<see cref="MongoDriverConfiguration"/>); <see cref="Report"/>
/// publishes provenance, reusing the fleet-shared <see cref="AdapterBootReporting.ResolveConnectionString"/>
/// for the boot-report connection display. <see cref="KoanModule.Id"/> preserves the prior ModuleName so
/// boot reports are unchanged.
/// </summary>
public sealed class MongoModule : KoanModule
{
    public override string Id => "Koan.Data.Connector.Mongo";

    public override void Register(IServiceCollection services)
    {
        // The Mongo-family global driver configuration must exist before any Mongo op during bootstrap
        // (Initialize-phase by necessity — KoanModule.Start is too late). Once-guarded + idempotent.
        MongoDriverConfiguration.EnsureApplied();

        // ServiceCollection-scoped services (run every time - naturally idempotent via TryAdd)
        services.AddKoanOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.AddSingleton<MongoClientProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAsyncAdapterInitializer, MongoClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAdapterReadiness, MongoClientProvider>());
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());

        // Register orchestration evaluator for dependency management
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, MongoOrchestrationEvaluator>());

        // Register MongoDB discovery adapter (maintains "Reference = Intent")
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MongoDiscoveryAdapter>());

        // Optional Zen Garden binding metadata (used only when Koan.ZenGarden is referenced). One binding:
        // the offering lookup keys on the adapter id "mongo" (MongoOptionsConfigurator), so the former
        // "mongodb"-aliased binding was dead — dropped (ARCH-0103 §L ZenGarden dedup).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IZenGardenOfferingBinding, MongoZenGardenOfferingBinding>());
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        // Autonomous discovery adapter handles all connection string resolution; the boot report shows
        // the discovery result via the fleet-shared AdapterBootReporting.ResolveConnectionString.
        module.AddNote("MongoDB discovery handled by autonomous MongoDiscoveryAdapter");

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

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSourceKey = connection.ResolvedKey ?? MongoItems.ConnectionString.Key;
        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;

        if (connectionIsAuto)
        {
            // Whitespace coalesce (not just null) to match the old ResolveConnectionStringForReporting
            // re-guard exactly: an explicit Database="" must still report against the default db.
            var resolvedDatabase = string.IsNullOrWhiteSpace(database.Value) ? defaultOptions.Database : database.Value;
            var username = Configuration.ReadFirst(
                cfg,
                "",
                Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Username),
                Infrastructure.ConfigurationConstants.DataFallback.Username);
            var password = Configuration.ReadFirst(
                cfg,
                "",
                Infrastructure.ConfigurationConstants.FullKey(Infrastructure.ConfigurationConstants.Keys.Password),
                Infrastructure.ConfigurationConstants.DataFallback.Password);

            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(resolvedDatabase)) parameters["database"] = resolvedDatabase!;
            if (!string.IsNullOrWhiteSpace(username)) parameters["username"] = username;
            if (!string.IsNullOrWhiteSpace(password)) parameters["password"] = password;

            var adapter = new MongoDiscoveryAdapter(cfg, NullLogger<MongoDiscoveryAdapter>.Instance);
            effectiveConnectionString = AdapterBootReporting.ResolveConnectionString(
                cfg,
                adapter,
                parameters,
                () => BuildFallbackConnectionString(defaultOptions, resolvedDatabase, username, password));
        }

        var connectionMode = connectionIsAuto
            ? ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true)
            : ProvenanceModes.FromConfigurationValue(connection);

        module.PublishConfigValue(
            MongoItems.ConnectionString,
            connection,
            displayOverride: effectiveConnectionString,
            modeOverride: connectionMode,
            usedDefaultOverride: connectionIsAuto ? true : connection.UsedDefault,
            sourceKeyOverride: connectionSourceKey);

        module.PublishConfigValue(MongoItems.Database, database, database.Value ?? defaultOptions.Database);

        module.AddSetting(
            MongoItems.EnsureCreatedSupported,
            ProvenanceModes.FromBootSource(BootSettingSource.Auto, usedDefault: true),
            true,
            usedDefault: true,
            sanitizeOverride: false);

        module.PublishConfigValue(MongoItems.DefaultPageSize, defaultPageSize);
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

        // ARCH-0068: Use static ConnectionStringParser for unified connection string building
        var components = new Koan.Core.Orchestration.ConnectionStringComponents(
            Host: "localhost",
            Port: 27017,
            Database: string.IsNullOrWhiteSpace(database) ? defaults.Database : database,
            Username: username,
            Password: password,
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        return Koan.Core.Orchestration.ConnectionStringParser.Build(components, "mongodb");
    }
}
