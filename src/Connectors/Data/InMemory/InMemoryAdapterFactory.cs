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
public sealed class InMemoryAdapterFactory : IDataAdapterFactory, IDataAdapterSetup
{
    public string Provider => Constants.Provider.Name;
    public IReadOnlyCollection<string> Aliases => [Constants.Provider.Alias];
    public IReadOnlyCollection<string> ReferenceIdentities => [Constants.Provider.ReferenceIdentity];

    public void DescribeClaims(IDataClaims claims) => InMemoryFeatures.Declare(claims);

    public DataAdapterSetupDescriptor DescribeSetup() => new("In-memory", []);

    public Task<DataProviderProbeResult> Probe(
        IServiceProvider services,
        DataProviderProbeContext candidate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(DataProviderProbeResult.Ready(
            "The in-memory provider is ready for this host lifetime."));
    }

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

    /// <summary>One host-owned store per source name, exactly as <see cref="InMemoryState"/> keys it.</summary>
    public string? ResolveStoreIdentity(IServiceProvider services, string source)
        => DataSourceRegistry.IdentityOf(
            "inmemory\n" + (string.IsNullOrWhiteSpace(source) ? Constants.Provider.DefaultSource : source));

    /// <summary>
    /// The source's store marker (DATA-0148), held beside its stores for the host's lifetime. A ReadOnly source reads it
    /// and never creates it.
    /// </summary>
    public ValueTask<string?> ReadStoreMarker(
        IServiceProvider services,
        string source,
        bool create,
        CancellationToken ct = default)
    {
        var resolved = string.IsNullOrWhiteSpace(source) ? Constants.Provider.DefaultSource : source;
        var definition = services.GetRequiredService<DataSourceRegistry>().GetSource(resolved);
        var allowed = create && (definition?.Access ?? DataSourceAccess.ReadWrite) == DataSourceAccess.ReadWrite &&
                      (definition?.StorageLifecycle ?? StorageLifecycle.Managed) == StorageLifecycle.Managed;
        return ValueTask.FromResult(services.GetRequiredService<InMemoryState>().Marker(resolved, allowed));
    }

    /// <summary>Each source name keys its own host-owned store, so two different identities are two stores.</summary>
    public ValueTask<bool> ProveDistinctStores(
        IServiceProvider services,
        string source,
        string other,
        CancellationToken ct = default) => ValueTask.FromResult(true);

    public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new()
    {
        Style = StorageNamingStyle.EntityType,
        Casing = NameCasing.AsIs,
        PartitionSeparator = '#',
        Partition = PartitionTokenPolicy.Default
    };
}
