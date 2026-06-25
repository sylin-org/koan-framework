using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Abstractions;

/// <summary>
/// The shared discovery / naming / source-routing surface for every Koan data adapter factory (ARCH-0103 §4.1 — the
/// Moniker contract). A factory <em>announces</em> its provider key (via <see cref="INamingProvider.Provider"/>) and
/// which provider strings it answers to (<see cref="CanHandle"/>); the framework ranks and routes through this one
/// surface regardless of whether the factory yields a record repository (<see cref="IDataAdapterFactory"/>) or a
/// vector repository (<c>IVectorAdapterFactory</c>).
/// </summary>
/// <remarks>
/// The two <c>Create</c> surfaces stay specialized on their concrete sub-interfaces because their return types
/// (<c>IDataRepository</c> vs <c>IVectorSearchRepository</c>) live in different assemblies — a single generic interface
/// returning both would form an assembly cycle (ARCH-0103 §8, rejected option b). This marker is the structural
/// realization of "one factory": it unifies the part that <em>can</em> be unified (discovery, naming, source-routing)
/// so the record plane (<c>AdapterResolver</c>) and the vector plane (<c>VectorService</c>) resolve a provider
/// identically — via the shared <c>FactoryResolver</c> ranking and the shared <c>RoutedSource</c> routing decision.
/// </remarks>
public interface IAdapterFactory : INamingProvider
{
    /// <summary>Whether this factory answers to the given provider string (e.g. "sqlite", "weaviate").</summary>
    bool CanHandle(string provider);
}
