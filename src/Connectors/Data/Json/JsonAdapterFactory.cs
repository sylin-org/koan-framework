using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Json.Runtime;

namespace Koan.Data.Connector.Json;

[ProviderPriority(0)]
public sealed class JsonAdapterFactory : IDataAdapterFactory
{
    public string Provider => Infrastructure.Constants.Provider.Name;
    public bool IsAutomaticFloor => true;
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.Json"];

    public void DescribeClaims(IDataClaims claims) => JsonFeatures.Declare(claims);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        if (sp.GetRequiredService<IDataMappingPlans>().Find<TEntity>(resolvedSource) is not null)
            throw new NotSupportedException(
                $"JSON does not expose a physical compatibility-mapping surface for '{typeof(TEntity).Name}'. " +
                "Remove Map<T>(...) or route the source to an adapter that supports physical mappings.");
        var config = sp.GetRequiredService<IConfiguration>();
        var sourceRegistry = sp.GetRequiredService<DataSourceRegistry>();
        var baseOpts = sp.GetRequiredService<IOptions<JsonDataOptions>>().Value;

        // Resolve source-specific directory path (JSON uses DirectoryPath instead of ConnectionString)
        var directoryPath = AdapterConnectionResolver.GetSourceSetting(
            config,
            sourceRegistry,
            Infrastructure.Constants.Provider.Name,
            resolvedSource,
            "DirectoryPath",
            baseOpts.DirectoryPath,
            this);

        var definition = sourceRegistry.GetSource(resolvedSource);
        var route = new JsonRoute(
            resolvedSource,
            directoryPath,
            definition?.StorageLifecycle ?? StorageLifecycle.Managed,
            definition?.Access ?? DataSourceAccess.ReadWrite);

        return new JsonRepository<TEntity, TKey>(
            route,
            sp.GetRequiredService<Koan.Data.Core.Semantics.DataSegmentationPlan>(),
            this,
            sp);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
        };
}

internal sealed record JsonRoute(
    string Source,
    string DirectoryPath,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access);
