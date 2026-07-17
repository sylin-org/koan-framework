using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Abstractions;

/// <summary>
/// The shared discovery / naming / source-routing surface for every Koan data adapter factory (ARCH-0103 §4.1 — the
/// Moniker contract). A factory <em>announces</em> its provider key (via <see cref="INamingProvider.Provider"/>) and
/// declarative aliases; the framework compiles and routes through this one
/// surface regardless of whether the factory yields a record repository (<see cref="IDataAdapterFactory"/>) or a
/// vector repository (<c>IVectorAdapterFactory</c>).
/// </summary>
/// <remarks>
/// The two <c>Create</c> surfaces stay specialized on their concrete sub-interfaces because their return types
/// (<c>IDataRepository</c> vs <c>IVectorSearchRepository</c>) live in different assemblies — a single generic interface
/// returning both would form an assembly cycle (ARCH-0103 §8, rejected option b). This marker is the structural
/// realization of "one factory": it unifies the part that <em>can</em> be unified (discovery, naming, source-routing)
/// so the record plane and vector plane consume the same Core provider-catalog identity and ordering law while
/// retaining their typed route policy through the shared <c>RoutedSource</c> decision.
/// </remarks>
public interface IAdapterFactory : INamingProvider
{
    /// <summary>Additional stable names accepted for this provider.</summary>
    IReadOnlyCollection<string> Aliases => [];

    /// <summary>Project/package identities that make this provider a direct application candidate.</summary>
    IReadOnlyCollection<string> ReferenceIdentities => [];

    /// <summary>
    /// Whether this provider is an automatic floor when no direct provider intent exists. Most connector
    /// packages are direct-only; a foundation bundle may nominate one deliberately safe floor.
    /// </summary>
    bool IsAutomaticFloor => false;
}
