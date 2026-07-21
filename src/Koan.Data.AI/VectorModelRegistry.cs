using Koan.Data.Core.Model;

namespace Koan.Data.AI;

/// <summary>
/// Durable, per-(entity, partition) record of which embedding model(s) have produced the vectors in
/// an index (AI-0036 P2 / W4 follow-up). It is the durable, O(1) backing the
/// <see cref="VectorModelGuard"/> hard throw needs: maintained incrementally as vectors are written
/// (one tiny record per entity+partition), so each guard reads it instead of scanning
/// <see cref="EmbeddingState{TEntity}"/>.
/// </summary>
/// <remarks>
/// Framework-managed, invisible to users. Partitioned like the vectors it describes (DATA-0077:
/// tenancy is a partition) — the registry record for partition P lives in P's store, keyed by P.
/// </remarks>
internal sealed class VectorModelRegistry<TEntity> : Entity<VectorModelRegistry<TEntity>>
    where TEntity : class
{
    /// <summary>The partition this registry describes ("" = default/no partition).</summary>
    public required string Partition { get; set; }

    /// <summary>The distinct embedding models that have produced vectors in this (entity, partition) index.</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>ID: "vmr:{entityType}:{partition}".</summary>
    public static string MakeId(string? partition)
        => $"vmr:{typeof(TEntity).Name}:{partition ?? string.Empty}";
}
