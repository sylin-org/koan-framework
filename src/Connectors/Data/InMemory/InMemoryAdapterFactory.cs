using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Connector.InMemory.Infrastructure;
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
