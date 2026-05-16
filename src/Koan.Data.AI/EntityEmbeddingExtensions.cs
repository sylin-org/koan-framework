using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;

namespace Koan.Data.AI;

/// <summary>
/// Extension methods for semantic search on entities.
/// Convention-first: works without [Embedding] attribute for on-demand operations.
/// [Embedding] attribute gates auto-embed-on-save, not on-demand search.
/// </summary>
public static class EntityEmbeddingExtensions
{
    /// <summary>
    /// Performs semantic search across entities.
    /// Generates embedding for query text and finds most similar entities.
    /// Works by convention — no [Embedding] attribute required for on-demand use.
    /// </summary>
    public static async Task<List<TEntity>> SemanticSearch<TEntity>(
        string query,
        int limit = 10,
        double threshold = 0.0,
        string? partition = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>, new()
    {
        var metadata = EmbeddingMetadata.Resolve<TEntity>();

        // Generate embedding for query text with source routing
        float[] queryEmbedding;
        using (metadata.Source != null || metadata.Model != null
            ? Client.Scope(all: metadata.Source)
            : null)
        {
            queryEmbedding = await Client.Embed(query, ct);
        }

        // Search for similar vectors
        var vectorResults = await Vector<TEntity>.Search(
            vector: queryEmbedding,
            topK: limit,
            ct: ct);

        // Load full entities from search results
        var entities = new List<TEntity>();
        foreach (var match in vectorResults.Matches)
        {
            if (match.Score < threshold)
                continue;

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
    /// Works by convention — no [Embedding] attribute required for on-demand use.
    /// </summary>
    public static async Task<List<TEntity>> FindSimilar<TEntity>(
        this TEntity entity,
        int limit = 10,
        double threshold = 0.7,
        bool includeSource = false,
        string? partition = null,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>, new()
    {
        var metadata = EmbeddingMetadata.Resolve<TEntity>();

        // Generate embedding from entity content with source routing
        var text = metadata.BuildEmbeddingText(entity);
        float[] queryEmbedding;
        using (metadata.Source != null || metadata.Model != null
            ? Client.Scope(all: metadata.Source)
            : null)
        {
            queryEmbedding = await Client.Embed(text, ct);
        }

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
                continue;

            if (!includeSource && match.Id == entity.Id)
            {
                continue;
            }

            var similarEntity = await LoadEntity<TEntity>(match.Id, partition, ct);
            if (similarEntity != null)
            {
                entities.Add(similarEntity);

                if (entities.Count >= limit)
                {
                    break;
                }
            }
        }

        return entities;
    }

    private static async Task<TEntity?> LoadEntity<TEntity>(
        string entityId,
        string? partition,
        CancellationToken ct)
        where TEntity : class, IEntity<string>, new()
    {
        if (string.IsNullOrEmpty(partition))
        {
            return await Data<TEntity, string>.Get(entityId, ct);
        }
        else
        {
            using (EntityContext.Partition(partition))
            {
                return await Data<TEntity, string>.Get(entityId, ct);
            }
        }
    }
}
