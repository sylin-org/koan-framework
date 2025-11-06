using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;

namespace Koan.Data.AI;

/// <summary>
/// Extension methods for semantic search on entities with [Embedding] attribute.
/// Part of ARCH-0070: Attribute-Driven AI Embeddings (Phase 2).
/// </summary>
public static class EntityEmbeddingExtensions
{
    /// <summary>
    /// Performs semantic search across entities with [Embedding] attribute.
    /// Generates embedding for query text and finds most similar entities.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
    /// <param name="query">Natural language search query</param>
    /// <param name="limit">Maximum number of results (default: 10)</param>
    /// <param name="threshold">Minimum similarity score 0-1 (default: 0.0)</param>
    /// <param name="partition">Optional partition to search within</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of entities ordered by relevance</returns>
    /// <exception cref="InvalidOperationException">If entity type lacks [Embedding] attribute</exception>
    public static async Task<List<TEntity>> SemanticSearch<TEntity>(
        string query,
        int limit = 10,
        double threshold = 0.0,
        string? partition = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>, new()
    {
        // Verify entity has [Embedding] attribute
        var metadata = EmbeddingMetadata.Get<TEntity>();
        if (metadata == null)
        {
            throw new InvalidOperationException(
                $"Type {typeof(TEntity).Name} does not have [Embedding] attribute. " +
                "Add [Embedding] to enable semantic search.");
        }

        // Generate embedding for query text
        // TODO: Use metadata.Model when Ai.Embed supports model parameter
        var queryEmbedding = await Ai.Embed(query, ct);

        // Search for similar vectors
        var vectorResults = await Vector<TEntity>.Search(
            vector: queryEmbedding,
            topK: limit,
            ct: ct);

        // Load full entities from search results
        // Vector IDs are the entity IDs directly
        var entities = new List<TEntity>();
        foreach (var match in vectorResults.Matches)
        {
            if (match.Score < threshold)
                continue; // Skip results below threshold

            var entity = await LoadEntity<TEntity>(match.Id, partition, ct);
            if (entity != null)
            {
                entities.Add(entity);
            }
        }

        return entities;
    }

    /// <summary>
    /// Finds entities similar to the current entity based on embedding similarity.
    /// Generates embedding from entity content and searches for similar vectors.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [Embedding] attribute</typeparam>
    /// <param name="entity">Source entity to find similar items for</param>
    /// <param name="limit">Maximum number of results (default: 10)</param>
    /// <param name="threshold">Minimum similarity score 0-1 (default: 0.7)</param>
    /// <param name="includeSource">Include source entity in results (default: false)</param>
    /// <param name="partition">Optional partition to search within</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of similar entities ordered by relevance</returns>
    /// <exception cref="InvalidOperationException">If entity type lacks [Embedding] attribute</exception>
    public static async Task<List<TEntity>> FindSimilar<TEntity>(
        this TEntity entity,
        int limit = 10,
        double threshold = 0.7,
        bool includeSource = false,
        string? partition = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>, new()
    {
        // Verify entity has [Embedding] attribute
        var metadata = EmbeddingMetadata.Get<TEntity>();
        if (metadata == null)
        {
            throw new InvalidOperationException(
                $"Type {typeof(TEntity).Name} does not have [Embedding] attribute. " +
                "Add [Embedding] to enable FindSimilar.");
        }

        // Generate embedding from entity content
        var text = metadata.BuildEmbeddingText(entity);
        // TODO: Use metadata.Model when Ai.Embed supports model parameter
        var queryEmbedding = await Ai.Embed(text, ct);

        // Search for similar vectors (fetch +1 to account for filtering source)
        var searchLimit = includeSource ? limit : limit + 1;
        var vectorResults = await Vector<TEntity>.Search(
            vector: queryEmbedding,
            topK: searchLimit,
            ct: ct);

        // Load full entities and optionally filter out source
        var entities = new List<TEntity>();
        foreach (var match in vectorResults.Matches)
        {
            if (match.Score < threshold)
                continue; // Skip results below threshold

            if (!includeSource && match.Id == entity.Id)
            {
                continue; // Skip source entity
            }

            var similarEntity = await LoadEntity<TEntity>(match.Id, partition, ct);
            if (similarEntity != null)
            {
                entities.Add(similarEntity);

                if (entities.Count >= limit)
                {
                    break; // Reached desired limit
                }
            }
        }

        return entities;
    }

    /// <summary>
    /// Helper method to load entity by ID, handling partition context.
    /// </summary>
    private static async Task<TEntity?> LoadEntity<TEntity>(
        string entityId,
        string? partition,
        CancellationToken ct)
        where TEntity : class, IEntity<string>, new()
    {
        // Vector search returns entity IDs directly
        if (string.IsNullOrEmpty(partition))
        {
            return await Data<TEntity, string>.GetAsync(entityId, ct);
        }
        else
        {
            using (EntityContext.Partition(partition))
            {
                return await Data<TEntity, string>.GetAsync(entityId, ct);
            }
        }
    }
}
