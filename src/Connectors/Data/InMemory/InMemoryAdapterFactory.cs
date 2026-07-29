using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Connector.InMemory.Infrastructure;
using Koan.Data.Connector.InMemory.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// Data adapter factory for explicitly ephemeral, process-local storage.
/// </summary>
[ProviderPriority(Constants.Provider.Priority)]
public sealed class InMemoryAdapterFactory : IDataAdapterFactory
{
    public string Provider => Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => [Constants.Provider.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => ["Koan.Data.Connector.InMemory"];

    public void DescribeClaims(IDataClaims claims) => InMemoryFeatures.Declare(claims);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        if (sp.GetRequiredService<IDataMappingPlans>().Find<TEntity>(resolvedSource) is not null)
            throw new NotSupportedException(
                $"InMemory does not expose a physical compatibility-mapping surface for '{typeof(TEntity).Name}'. " +
                "Remove Map<T>(...) or route the source to an adapter that supports physical mappings.");
        var definition = sp.GetRequiredService<DataSourceRegistry>().GetSource(resolvedSource);
        if (definition?.StorageLifecycle == Koan.Data.Abstractions.Sources.StorageLifecycle.External)
            throw new NotSupportedException(
                $"InMemory cannot open source '{resolvedSource}' as External because it owns only host-ephemeral stores. " +
                "Use StorageLifecycle=Managed or select an adapter that can open provider-owned storage.");
        var dataStore = sp.GetRequiredService<InMemoryDataStore>();
        return new InMemoryRepository<TEntity, TKey>(dataStore, resolvedSource);
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
