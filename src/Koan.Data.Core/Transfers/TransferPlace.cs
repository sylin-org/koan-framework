using Koan.Data.Abstractions;
using Koan.Data.Core.Routing;

namespace Koan.Data.Core.Transfers;

/// <summary>
/// Where an Entity operation lands under the ambient <see cref="EntityContext"/>: the elected provider, the physical
/// store its route opens, and the container the provider names for the Entity and partition. Two endpoints that
/// differ as written (a source spelled in another case, an explicit Default, an inherited partition, a source without
/// its own connection) can be one place; comparing places, not declarations, is what keeps a transfer from writing a
/// container onto itself.
/// </summary>
internal sealed record TransferPlace(
    string Provider,
    string Source,
    string? Store,
    string Container,
    DataRouteBinding? Binding,
    IDataAdapterFactory Factory)
{
    /// <summary>The place as a transfer result reports it.</summary>
    public TransferEndpoint Endpoint => new(Source, Provider, Container);

    /// <summary>Resolves the place under the current ambient context. Performs no I/O.</summary>
    public static TransferPlace Resolve<TEntity>(IServiceProvider services, DataSourceRegistry sources)
        where TEntity : class
    {
        var decision = AdapterResolver.ResolveDecisionForEntity<TEntity>(services, sources);
        var store = decision.Factory.ResolveStoreIdentity(services, decision.Source)
                    ?? decision.Binding?.Plan.ConnectionIdentity;
        var container = decision.Factory.ResolveStorage(
            typeof(TEntity),
            EntityContext.Current?.Partition,
            services);
        return new TransferPlace(decision.Adapter, decision.Source, store, container, decision.Binding, decision.Factory);
    }

    /// <summary>
    /// True when the two places are proven to be different containers: different providers, one store with different
    /// containers, or two stores proven apart. Two store identities that merely differ are not proof, because connection
    /// text can spell one store two ways (NK-305). The stores' markers answer first (DATA-0148): the same marker is one
    /// store, so only different containers in it are distinct; different markers are two stores. Without a marker on
    /// both sides the adapter's own proof (locks, server identity) decides, and an unproven pair is not distinct.
    /// </summary>
    public async ValueTask<bool> IsProvenDistinctFrom(TransferPlace other, IServiceProvider services, CancellationToken ct)
    {
        if (IsSameAs(other)) return false;
        if (!string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(Store, other.Store, StringComparison.Ordinal)) return true;
        switch (await StoreMarkers.Compare(Factory, Source, other.Factory, other.Source, services, ct).ConfigureAwait(false))
        {
            case StoreKinship.Same:
                return !string.Equals(Container, other.Container, StringComparison.OrdinalIgnoreCase);
            case StoreKinship.Different:
                return true;
        }
        try
        {
            return await Factory.ProveDistinctStores(services, Source, other.Source, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when both places are, or may be, one physical container. Containers compare without regard to letter
    /// case because several stores (SQLite, SQL Server, MySQL on Windows, a JSON directory on a case-insensitive file
    /// system) resolve names that differ only by case to one object; an unknown store is assumed shared.
    /// </summary>
    public bool IsSameAs(TransferPlace other)
        => string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Container, other.Container, StringComparison.OrdinalIgnoreCase)
           && (Store is null || other.Store is null || string.Equals(Store, other.Store, StringComparison.Ordinal));
}
