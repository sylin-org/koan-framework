using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory;

/// <summary>
/// Data adapter factory for in-memory storage.
/// Priority: -100 (lowest) to act as fallback when no other adapter is configured.
/// </summary>
[ProviderPriority(-100)]
public sealed class InMemoryAdapterFactory : IDataAdapterFactory
{
    public string Provider => "inmemory";
    public IReadOnlyCollection<string> Aliases => ["memory"];

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider sp,
        string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        // The singleton store; the repo resolves its per-(source, partition) physical store from the ambient context
        // on each op (ARCH-0103: Database mode = per routed source, Container mode = per ambient partition).
        var dataStore = sp.GetRequiredService<InMemoryDataStore>();
        return new InMemoryRepository<TEntity, TKey>(dataStore, source);
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
