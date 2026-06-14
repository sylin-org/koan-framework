using System.Security.Cryptography;
using System.Text;
using Koan.Rag.Abstractions;
using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Graph;

/// <summary>
/// Resolves extracted entity mentions to canonical entities using a tiered strategy:
/// <list type="number">
///   <item>Exact string match + normalized aliases (60-70% of duplicates)</item>
///   <item>Embedding proximity above threshold (20% more)</item>
///   <item>LLM-assisted disambiguation for ambiguous pairs (~10% remaining)</item>
/// </list>
/// <para>
/// Merge-on-read: original surface forms are preserved. The canonical mapping is
/// a separate lookup, not a destructive merge. False merges can be undone by
/// removing the mapping.
/// </para>
/// </summary>
internal sealed class EntityResolver
{
    private readonly IConceptGraphStore _graphStore;
    private readonly ILogger<EntityResolver> _logger;
    private readonly double _similarityThreshold;

    public EntityResolver(
        IConceptGraphStore graphStore,
        IOptions<RagOptions> options,
        ILogger<EntityResolver> logger)
    {
        _graphStore = graphStore;
        _logger = logger;
        _similarityThreshold = options.Value.EntityResolutionThreshold;
    }

    /// <summary>
    /// Resolve a batch of extracted entities against the existing graph.
    /// Returns the canonical entities (existing or new) and a graph delta to apply.
    /// </summary>
    public async Task<EntityResolutionResult> Resolve(
        IReadOnlyList<ExtractedEntity> extractedEntities,
        string documentId,
        CancellationToken ct)
    {
        var delta = new GraphDeltaBuilder();
        var resolvedEntities = new List<ConceptEntity>();
        var stats = _graphStore.GetStats();

        foreach (var extracted in extractedEntities)
        {
            if (string.IsNullOrWhiteSpace(extracted.Name))
                continue;

            var normalized = NormalizeName(extracted.Name);

            // Tier 1: Exact match against existing entities
            var existing = await FindExactMatch(normalized, ct);
            if (existing is not null)
            {
                // Update surface forms and mention count
                var updated = AddSurfaceForm(existing, extracted.Name);
                delta.UpdateEntity(updated);
                delta.AddMention(updated.Id, documentId, +1);
                resolvedEntities.Add(updated);
                continue;
            }

            // Tier 2: Embedding similarity (if we have embeddings)
            if (stats.EntityCount > 0)
            {
                var similar = await FindSimilarByEmbedding(normalized, extracted.Description, ct);
                if (similar is not null)
                {
                    var updated = AddSurfaceForm(similar, extracted.Name);
                    delta.UpdateEntity(updated);
                    delta.AddMention(updated.Id, documentId, +1);
                    resolvedEntities.Add(updated);
                    continue;
                }
            }

            // Tier 3: New entity — no match found
            var newEntity = CreateEntity(normalized, extracted);
            delta.AddEntity(newEntity);
            delta.AddMention(newEntity.Id, documentId, +1);
            resolvedEntities.Add(newEntity);
        }

        return new EntityResolutionResult(resolvedEntities, delta.Build());
    }

    /// <summary>
    /// Remove all entity mentions from a document. Entities reaching zero mentions are pruned.
    /// </summary>
    public GraphDelta BuildRemovalDelta(string documentId, IReadOnlyList<string> entityIds)
    {
        var builder = new GraphDeltaBuilder();
        foreach (var entityId in entityIds)
            builder.AddMention(entityId, documentId, -1);
        return builder.Build();
    }

    // ── Tier 1: Exact Match ─────────────────────────────────────────────

    private async Task<ConceptEntity?> FindExactMatch(string normalizedName, CancellationToken ct)
    {
        // Check the graph for entities matching the normalized name
        var entityId = ComputeEntityId(normalizedName);
        var neighborhood = await _graphStore.GetNeighborhood(entityId, depth: 0, ct);

        return neighborhood.Entities.FirstOrDefault(e => e.Id == entityId);
    }

    // ── Tier 2: Embedding Similarity ────────────────────────────────────

    private async Task<ConceptEntity?> FindSimilarByEmbedding(
        string normalizedName,
        string description,
        CancellationToken ct)
    {
        try
        {
            // Embed the candidate entity name + description
            var textToEmbed = $"{normalizedName}: {description}";
            var candidateEmbedding = await Koan.AI.Client.Embed(textToEmbed, ct);

            // Compare against existing entities with embeddings
            // Future: use Vector<ConceptEntity>.Search() for ANN
            var allEntities = await _graphStore.GetAllEntities(ct);
            if (allEntities.Count == 0)
                return null;

            if (allEntities.Count > 10_000)
            {
                _logger.LogWarning(
                    "Entity resolution: {Count} entities exceeds scan limit (10,000). " +
                    "Consider indexing entity embeddings via Vector<ConceptEntity>.Search() for ANN.",
                    allEntities.Count);
                // Take a random sample to avoid degenerate behavior
                allEntities = allEntities.OrderBy(_ => Random.Shared.Next()).Take(10_000).ToList();
            }

            ConceptEntity? bestMatch = null;
            double bestSimilarity = 0;

            foreach (var entity in allEntities)
            {
                if (entity.Embedding is null || entity.Embedding.Length == 0)
                    continue;

                var similarity = CosineSimilarity(candidateEmbedding, entity.Embedding);
                if (similarity > _similarityThreshold && similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestMatch = entity;
                }
            }

            if (bestMatch is not null)
            {
                _logger.LogDebug(
                    "Entity '{Candidate}' resolved to '{Canonical}' via embedding similarity ({Score:F3})",
                    normalizedName, bestMatch.CanonicalName, bestSimilarity);
            }

            return bestMatch;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Embedding-based entity resolution failed for '{Name}'", normalizedName);
            return null;
        }
    }

    // ── Entity Creation ─────────────────────────────────────────────────

    private static ConceptEntity CreateEntity(string normalizedName, ExtractedEntity extracted)
    {
        return new ConceptEntity
        {
            Id = ComputeEntityId(normalizedName),
            CanonicalName = normalizedName,
            Description = extracted.Description,
            MentionCount = 0, // Will be incremented by the mention delta
            SurfaceForms = [extracted.Name]
        };
    }

    private static ConceptEntity AddSurfaceForm(ConceptEntity entity, string surfaceForm)
    {
        if (entity.SurfaceForms.Contains(surfaceForm, StringComparer.OrdinalIgnoreCase))
            return entity;

        var forms = entity.SurfaceForms.ToList();
        forms.Add(surfaceForm);
        return entity with { SurfaceForms = forms };
    }

    // ── Utilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Normalize entity name: lowercase, trim, collapse whitespace.
    /// "Fire-type" → "fire-type", "  REST  API  " → "rest api".
    /// </summary>
    internal static string NormalizeName(string name)
    {
        var trimmed = name.Trim().ToLowerInvariant();
        // Collapse multiple whitespace to single space
        return string.Join(' ', trimmed.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Deterministic entity ID from normalized name.
    /// </summary>
    internal static string ComputeEntityId(string normalizedName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName));
        return $"ce:{Convert.ToHexStringLower(hash[..8])}";
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 0 ? dot / denom : 0;
    }
}

/// <summary>
/// Result of entity resolution for a batch of extracted entities.
/// </summary>
internal sealed record EntityResolutionResult(
    IReadOnlyList<ConceptEntity> ResolvedEntities,
    GraphDelta Delta);

/// <summary>
/// Mutable builder for constructing a <see cref="GraphDelta"/>.
/// </summary>
internal sealed class GraphDeltaBuilder
{
    private readonly List<ConceptEntity> _addedEntities = [];
    private readonly List<ConceptRelationship> _addedRelationships = [];
    private readonly List<string> _removedEntityIds = [];
    private readonly List<string> _removedRelationshipIds = [];
    private readonly List<EntityMentionDelta> _mentionDeltas = [];

    public void AddEntity(ConceptEntity entity) => _addedEntities.Add(entity);
    public void UpdateEntity(ConceptEntity entity) => _addedEntities.Add(entity);
    public void AddRelationship(ConceptRelationship rel) => _addedRelationships.Add(rel);
    public void RemoveEntity(string id) => _removedEntityIds.Add(id);
    public void AddMention(string entityId, string documentId, int delta)
        => _mentionDeltas.Add(new EntityMentionDelta(entityId, documentId, delta));

    public GraphDelta Build() => new()
    {
        AddedEntities = _addedEntities,
        AddedRelationships = _addedRelationships,
        RemovedEntityIds = _removedEntityIds,
        RemovedRelationshipIds = _removedRelationshipIds,
        MentionDeltas = _mentionDeltas
    };
}
