using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Data.Core.Model;
using Koan.Data.Vector;

namespace Koan.Data.AI;

/// <summary>One semantic-search hit: the loaded Entity and its similarity to the query.</summary>
public sealed record ScoredMatch<TModel>(TModel Entity, double Similarity)
    where TModel : class, IEntity<string>, new();

/// <summary>
/// Type-scoped AI operations for one Entity kind: <c>Note.Ai.Search("…")</c>, <c>Note.Ai.Embed(note)</c>.
/// Thin router over the AI client and the vector facade, bound to the model's declared embedding
/// configuration (<c>[Embedding]</c> when present, convention-first otherwise). Present whenever
/// <c>Sylin.Koan.Data.AI</c> is referenced. Per-instance similarity is the entity's own verb:
/// <c>note.Similar(...)</c> in <see cref="EntityEmbeddingExtensions"/>.
/// </summary>
public readonly struct AiStatics<TModel>
    where TModel : Entity<TModel>, new()
{
    /// <summary>Embed one instance's content using this kind's declared embedding configuration.</summary>
    public Task<float[]> Embed(TModel entity, CancellationToken ct = default)
        => EntityAi.Embed(entity, ct);

    /// <summary>Semantic search across this kind: <c>Todo.Ai.Search("errands around the house", s => s.Top(10), ct)</c>.
    /// Embeds the query with the kind's declared model, finds the nearest vectors, loads the entities.
    /// Convention-first — no [Embedding] attribute required.</summary>
    public async Task<IReadOnlyList<TModel>> Search(
        string query,
        Action<SemanticSearchQuery>? configure = null,
        CancellationToken ct = default)
    {
        var declaration = Declare(configure);
        return await EntityEmbeddingExtensions.SemanticSearch<TModel>(
            query, declaration.TopCount, declaration.MinimumSimilarity, declaration.PartitionName, ct);
    }

    /// <summary>Semantic search with similarity scores attached — the dashboard-shaped variant:
    /// <c>Todo.Ai.SearchScored("quick wins", s => s.Top(10).Threshold(0.7), ct)</c>.</summary>
    public async Task<IReadOnlyList<ScoredMatch<TModel>>> SearchScored(
        string query,
        Action<SemanticSearchQuery>? configure = null,
        CancellationToken ct = default)
    {
        var declaration = Declare(configure);
        var matches = await EntityEmbeddingExtensions.SemanticSearchScored<TModel>(
            query, declaration.TopCount, declaration.MinimumSimilarity, declaration.PartitionName, ct);
        return matches.Select(m => new ScoredMatch<TModel>(m.Entity, m.Similarity)).ToList();
    }

    private static SemanticSearchQuery Declare(Action<SemanticSearchQuery>? configure)
    {
        var declaration = new SemanticSearchQuery();
        configure?.Invoke(declaration);
        return declaration;
    }
}

/// <summary>
/// Delivers the type-scoped <c>Note.Ai</c> gateway as a C# 14 static extension member — every
/// Entity kind gains it when <c>Sylin.Koan.Data.AI</c> is referenced, and it is absent without it
/// (Reference = Intent). Thin router over <see cref="EntityAi"/> and the vector facade.
/// </summary>
public static class EntityAiGatewayExtensions
{
    extension<T>(T) where T : Entity<T>, new()
    {
        /// <summary>AI operations for this Entity kind: embed and semantic search, bound to the
        /// kind's declared embedding configuration.</summary>
        public static AiStatics<T> Ai => default;
    }
}
