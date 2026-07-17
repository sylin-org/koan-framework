using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Qdrant.Discovery;

namespace Koan.Data.Vector.Connector.Qdrant.Initialization;

public sealed class QdrantVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<QdrantOptions>(Infrastructure.Constants.Section);
        services.AddSingleton<IConfigureOptions<QdrantOptions>, QdrantOptionsConfigurator>();

        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, QdrantHealthContributor>());

        // Reference = Intent: adding this package automatically enables Qdrant discovery so
        // ConnectionString="auto" resolves without further configuration.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, QdrantDiscoveryAdapter>());

        services.AddSingleton<IVectorAdapterFactory, QdrantVectorAdapterFactory>();
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Qdrant discovery handled by autonomous QdrantDiscoveryAdapter");

        var defaultOptions = new QdrantOptions();

        var connection = Configuration.ReadFirstWithSource(
            cfg,
            defaultOptions.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            "ConnectionStrings:Qdrant");

        var endpoint = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            defaultOptions.Endpoint);

        var distance = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.Distance,
            defaultOptions.Distance);

        var vectorField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.VectorField,
            defaultOptions.VectorField);

        var metadataField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.MetadataField,
            defaultOptions.MetadataField);

        var idField = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.IdField,
            defaultOptions.IdField);

        var timeoutSeconds = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaultOptions.DefaultTimeoutSeconds);

        var autoCreate = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.AutoCreateCollection,
            defaultOptions.AutoCreateCollection);

        var waitForResult = Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Keys.WaitForResult,
            defaultOptions.WaitForResult);

        var connectionIsAuto = string.IsNullOrWhiteSpace(connection.Value) || string.Equals(connection.Value, "auto", StringComparison.OrdinalIgnoreCase);
        var connectionSource = connectionIsAuto ? BootSettingSource.Auto : connection.Source;
        var connectionSourceKey = connection.ResolvedKey ?? Infrastructure.Constants.Configuration.Keys.ConnectionString;

        var effectiveConnectionString = connection.Value ?? defaultOptions.ConnectionString;
        if (connectionIsAuto)
        {
            var adapter = new QdrantDiscoveryAdapter(cfg, NullLogger<QdrantDiscoveryAdapter>.Instance);
            effectiveConnectionString = ServiceDiscoveryReporting.ResolveConnectionString(
                cfg,
                adapter,
                null,
                () => NormalizeQdrantEndpoint(endpoint.Value ?? defaultOptions.Endpoint));
        }

        var sanitizedConnection = Redaction.DeIdentify(effectiveConnectionString);

        module.AddSetting(
            "ConnectionString",
            sanitizedConnection,
            source: connectionSource,
            consumers: new[]
            {
                "Koan.Data.Vector.Connector.Qdrant.QdrantOptionsConfigurator",
                "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory"
            },
            sourceKey: connectionSourceKey);

        module.AddSetting("Endpoint", endpoint.Value, source: endpoint.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("Distance", distance.Value, source: distance.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("VectorField", vectorField.Value, source: vectorField.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("MetadataField", metadataField.Value, source: metadataField.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("IdField", idField.Value, source: idField.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("TimeoutSeconds", timeoutSeconds.Value.ToString(), source: timeoutSeconds.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("AutoCreateCollection", autoCreate.Value ? "true" : "false", source: autoCreate.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
        module.AddSetting("WaitForResult", waitForResult.Value ? "true" : "false", source: waitForResult.Source,
            consumers: new[] { "Koan.Data.Vector.Connector.Qdrant.QdrantVectorAdapterFactory" });
    }

    private static string NormalizeQdrantEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "http://localhost:6333";
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var port = uri.IsDefaultPort ? Infrastructure.Constants.DefaultPort : uri.Port;
            return $"{uri.Scheme}://{uri.Host}:{port}";
        }

        return endpoint;
    }
}
