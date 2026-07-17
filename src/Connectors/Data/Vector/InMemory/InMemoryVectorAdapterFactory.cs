using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Connector.InMemory;

/// <summary>
/// In-process, in-memory <see cref="IVectorAdapterFactory"/> — the zero-infrastructure vector floor.
/// Issues per-(entity, partition) <see cref="InMemoryVectorRepository{TEntity, TKey}"/> stores keyed by
/// the adapter's own <see cref="INamingProvider.ResolveStorage"/> output, so partition isolation works
/// with no external infrastructure. Reference = Intent: referencing this package makes a single managed
/// binary capable of semantic search (k-NN over <see cref="System.Numerics.Tensors.TensorPrimitives"/>).
/// </summary>
/// <remarks>
/// Priority −100 mirrors the in-memory data adapter: it is the fallback that activates only when no
/// other vector provider is configured (a real server, or sqlite-vec for the durable in-proc tier).
/// This is also the cross-adapter convergence oracle — every native provider's pushdown is validated
/// against the result this adapter produces in managed code.
/// </remarks>
[ProviderPriority(-100)]
public sealed class InMemoryVectorAdapterFactory : IVectorAdapterFactory
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (float[] Embedding, object? Metadata)>> _stores
        = new(StringComparer.Ordinal);

    public string Provider => "inmemory";
    public IReadOnlyCollection<string> Aliases => ["memory", "inproc"];

    public StorageNamingCapability GetNamingCapability(IServiceProvider services)
        => new()
        {
            Style = StorageNamingStyle.EntityType,
            Casing = NameCasing.AsIs,
            PartitionSeparator = '#',
            Partition = PartitionTokenPolicy.Default,
        };

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        // ARCH-0103 P1 (Moniker): the routed source prefixes the store key, so a Database-mode axis lands each ambient's
        // embeddings in a distinct in-memory store (native-or-emulated: the emulation is a source-keyed dictionary).
        => new InMemoryVectorRepository<TEntity, TKey>(this, sp, _stores, source);

    /// <summary>Clears every per-(entity, partition) store this factory has issued (in-memory reset).</summary>
    public void ClearAll() => _stores.Clear();
}
