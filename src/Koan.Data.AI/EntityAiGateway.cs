using Koan.Data.Abstractions;
using Koan.Data.AI;
using Koan.Data.Core.Model;
using Koan.Data.Vector;

namespace Koan.Data.AI;

/// <summary>One semantic-search hit: the loaded Entity and its similarity to the query.</summary>
public sealed record ScoredMatch<TModel>(TModel Entity, double Similarity)
    where TModel : class, IEntity<string>, new();

/// <summary>
/// Type-scoped AI operations for one Entity kind: <c>Note.AI.Search("…")</c>, <c>Note.AI.Embed(note)</c>.
/// Thin router over the AI client and the vector facade, bound to the model's declared embedding
/// configuration (<c>[Embedding]</c> when present, convention-first otherwise). Present whenever
/// <c>Sylin.Koan.Data.AI</c> is referenced.
/// </summary>
public readonly struct AiStatics<TModel>
    where TModel : Entity<TModel>, new()
{
    /// <summary>Embed one instance's content using this kind's declared embedding configuration.</summary>
    public Task<float[]> Embed(TModel entity, CancellationToken ct = default)
        => EntityAi.Embed(entity, ct);

    /// <summary>Semantic search across this kind: embed the query with the declared model, find the
    /// nearest vectors, load the entities. Convention-first — no [Embedding] attribute required.</summary>
    public async Task<IReadOnlyList<TModel>> Search(string query, int limit = 10, CancellationToken ct = default)
        => await EntityEmbeddingExtensions.SemanticSearch<TModel>(query, limit, ct: ct);

    /// <summary>Semantic search with similarity scores attached — the dashboard-shaped variant.</summary>
    public async Task<IReadOnlyList<ScoredMatch<TModel>>> SearchScored(string query, int limit = 10, CancellationToken ct = default)
    {
        var matches = await EntityEmbeddingExtensions.SemanticSearchScored<TModel>(query, limit, ct: ct);
        return matches.Select(m => new ScoredMatch<TModel>(m.Entity, m.Similarity)).ToList();
    }

    /// <summary>Find entities semantically similar to one instance, excluding itself by default.</summary>
    public async Task<IReadOnlyList<TModel>> Similar(TModel entity, int limit = 10, double threshold = 0.7, CancellationToken ct = default)
        => await EntityEmbeddingExtensions.FindSimilar(entity, limit, threshold, ct: ct);
}

/// <summary>
/// Delivers the type-scoped <c>Note.AI</c> gateway as a C# 14 static extension member — every
/// Entity kind gains it when <c>Sylin.Koan.Data.AI</c> is referenced, and it is absent without it
/// (Reference = Intent). Thin router over <see cref="EntityAi"/> and the vector facade.
/// </summary>
public static class EntityAiGatewayExtensions
{
    extension<T>(T) where T : Entity<T>, new()
    {
        /// <summary>AI operations for this Entity kind: embed and semantic search, bound to the
        /// kind's declared embedding configuration.</summary>
        public static AiStatics<T> AI => default;
    }
}
