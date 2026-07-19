using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Connector.Mongo.Discovery;
using MongoItems = Koan.Data.Connector.Mongo.Infrastructure.MongoProvenanceItems;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Koan.Data.Connector.Mongo.Initialization;

/// <summary>
/// The MongoDB connector boot module (ARCH-0086 / ARCH-0103 §L). Folds the former split between the
/// previous DI/boot-report owner (with inline static BSON config and bespoke connection re-discovery) and
/// the independently discovered optimization owner
/// into ONE <see cref="KoanModule"/>: <see cref="Register"/> wires the services and applies the
/// once-guarded driver configuration (<see cref="MongoDriverConfiguration"/>); <see cref="Report"/>
/// publishes provenance, reusing the fleet-shared <see cref="ServiceDiscoveryReporting.ResolveConnectionString"/>
/// for the boot-report connection display. <see cref="KoanModule.Id"/> preserves the prior ModuleName so
/// boot reports are unchanged.
/// </summary>
public sealed class MongoModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // The Mongo-family global driver configuration must exist before any Mongo op during bootstrap
        // (Initialize-phase by necessity — KoanModule.Start is too late). Once-guarded + idempotent.
        MongoDriverConfiguration.EnsureApplied();

        // ServiceCollection-scoped services (run every time - naturally idempotent via TryAdd)
        services.AddKoanOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.AddSingleton<MongoClientProvider>();
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());

        // Register MongoDB discovery adapter (maintains "Reference = Intent")
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, MongoDiscoveryAdapter>());

    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        // Autonomous discovery adapter handles all connection string resolution; the boot report shows
        // the discovery result via the fleet-shared ServiceDiscoveryReporting.ResolveConnectionString.
        module.AddNote("MongoDB discovery handled by autonomous MongoDiscoveryAdapter");
        module.AddNote("Layered discovery: accepts compiled automatic sources through the shared Mongo discovery pipeline");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        var defaultOptions = new MongoOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            MongoItems.ConnectionStringKeys);

        var database = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.Database,
            MongoItems.DatabaseKeys);

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
                Infrastructure.Constants.Configuration.Username,
                Infrastructure.Constants.Configuration.DefaultSourceUsername);
            var password = Configuration.ReadFirst(
                cfg,
                "",
                Infrastructure.Constants.Configuration.Password,
                Infrastructure.Constants.Configuration.DefaultSourcePassword);

            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(resolvedDatabase)) parameters["database"] = resolvedDatabase!;
            if (!string.IsNullOrWhiteSpace(username)) parameters["username"] = username;
            if (!string.IsNullOrWhiteSpace(password)) parameters["password"] = password;

            var adapter = new MongoDiscoveryAdapter(cfg, NullLogger<MongoDiscoveryAdapter>.Instance);
            effectiveConnectionString = ServiceDiscoveryReporting.ResolveConnectionString(
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
            Port: Infrastructure.Constants.Discovery.DefaultPort,
            Database: string.IsNullOrWhiteSpace(database) ? defaults.Database : database,
            Username: username,
            Password: password,
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        return Koan.Core.Orchestration.ConnectionStringParser.Build(components, "mongodb");
    }
}
