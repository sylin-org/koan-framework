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
    DataRouteBinding? Binding)
{
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
        return new TransferPlace(decision.Adapter, decision.Source, store, container, decision.Binding);
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
