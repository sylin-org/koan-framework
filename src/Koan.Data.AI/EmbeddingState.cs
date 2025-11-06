using Koan.Data.Core.Model;

namespace Koan.Data.AI;

/// <summary>
/// Framework-managed tracking of embedding state for entities.
/// Stores content signature to detect changes and avoid redundant embedding generation.
/// Invisible to users - managed automatically by the embedding system.
/// </summary>
internal class EmbeddingState<TEntity> : Entity<EmbeddingState<TEntity>>
    where TEntity : class
{
    /// <summary>
    /// ID of the entity this state tracks (Entity.Id).
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// SHA256 hash of the embedding text when last embedded.
    /// Used to detect content changes.
    /// </summary>
    public required string ContentSignature { get; set; }

    /// <summary>
    /// When the entity was last successfully embedded.
    /// </summary>
    public DateTimeOffset LastEmbeddedAt { get; set; }

    /// <summary>
    /// AI model used for embedding (e.g., "text-embedding-3-small").
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Creates ID for EmbeddingState: "embed:{entityType}:{entityId}"
    /// </summary>
    public static string MakeId(string entityId)
    {
        var entityType = typeof(TEntity).Name;
        return $"embed:{entityType}:{entityId}";
    }
}
