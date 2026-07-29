using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.InMemory.Infrastructure;
using Koan.Data.Connector.InMemory.Runtime;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory;

/// <summary>Creates explicitly ephemeral, host-owned Entity repositories.</summary>
[ProviderPriority(Constants.Provider.Priority)]
public sealed class InMemoryAdapterFactory : IDataAdapterFactory
{
    public string Provider => Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => [Constants.Provider.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => [Constants.Provider.ReferenceIdentity];

    public void DescribeClaims(IDataClaims claims) => InMemoryFeatures.Declare(claims);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(
        IServiceProvider services,
        string source = Constants.Provider.DefaultSource)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        var resolved = string.IsNullOrWhiteSpace(source) ? Constants.Provider.DefaultSource : source;
        if (services.GetRequiredService<IDataMappingPlans>().Find<TEntity>(resolved) is not null)
            throw new NotSupportedException(
                $"InMemory does not expose a physical compatibility-mapping surface for '{typeof(TEntity).Name}'. " +
                "Remove Map<T>(...) or select an adapter with provider-owned physical storage.");

        var definition = services.GetRequiredService<DataSourceRegistry>().GetSource(resolved);
        if (definition?.StorageLifecycle == StorageLifecycle.External)
            throw new NotSupportedException(
                $"InMemory cannot open source '{resolved}' as External because no provider-owned storage exists. " +
                "Use StorageLifecycle=Managed or select an adapter that can open external storage.");

        return new InMemoryRepository<TEntity, TKey>(
            services.GetRequiredService<InMemoryState>(),
            resolved);
    }

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = '#',
        Partition = PartitionTokenPolicy.Default
    };
}
