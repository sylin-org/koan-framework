using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Json.Runtime;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Json;

[ProviderPriority(Infrastructure.Constants.Provider.Priority)]
public sealed class JsonAdapterFactory : IDataAdapterFactory
{
    public string Provider => Infrastructure.Constants.Provider.Name;
    public bool IsAutomaticFloor => true;
    public IReadOnlyCollection<string> ReferenceIdentities =>
        [Infrastructure.Constants.Provider.ReferenceIdentity];

    public void DescribeClaims(IDataClaims claims) => JsonFeatures.Declare(claims);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Infrastructure.Constants.Provider.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source)
            ? Infrastructure.Constants.Provider.DefaultSource
            : source;
        if (services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(resolvedSource) is not null)
        {
            throw new NotSupportedException(
                $"JSON does not expose a physical compatibility-mapping surface for '{typeof(TEntity).Name}'. " +
                "Remove Map<T>(...) or route the source to an adapter that supports physical mappings.");
        }

        var route = JsonRoute.Resolve(
            services.GetRequiredService<IConfiguration>(),
            services.GetRequiredService<DataSourceRegistry>(),
            services.GetRequiredService<IOptions<JsonDataOptions>>().Value,
            this,
            resolvedSource);

        return new JsonRepository<TEntity, TKey>(
            route,
            services.GetRequiredService<JsonFileRegistry>(),
            services.GetRequiredService<Koan.Data.Core.Semantics.DataSegmentationPlan>(),
            this,
            services);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = Infrastructure.Constants.Storage.PartitionSeparator,
        Partition = PartitionTokenPolicy.Default
    };
}
