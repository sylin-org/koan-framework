namespace Koan.Data.SearchEngine;

/// <summary>
/// Shared base for the Elasticsearch and OpenSearch vector option classes (DATA-0103). Carries every
/// field the shared <see cref="SearchEngineVectorRepository{TEntity,TKey}"/> reads, plus the single
/// <see cref="DefaultPageSize"/> the <c>IAdapterOptions</c> contract requires. Concrete classes add
/// only the per-package binding members (<c>ConnectionString</c>, <c>Readiness</c>) and the
/// <c>IAdapterOptions</c> implementation.
/// </summary>
/// <remarks>
/// This collapses the options drift the twins accumulated: Elasticsearch carried dead
/// <c>DefaultTopK</c>/<c>MaxTopK</c> knobs (referenced nowhere, aliased into <c>DefaultPageSize</c>)
/// while OpenSearch carried a plain <c>DefaultPageSize = 50</c>. Both are unified here onto the
/// single plain <c>DefaultPageSize = 50</c> semantics every other Koan data adapter uses.
/// </remarks>
public abstract class SearchEngineVectorOptions : ISearchEngineVectorOptions
{
    public string Endpoint { get; set; } = "http://localhost:9200";
    public string? IndexPrefix { get; set; } = "koan";
    public string? IndexName { get; set; } = null;
    public string VectorField { get; set; } = "embedding";
    public string MetadataField { get; set; } = "metadata";
    public string IdField { get; set; } = "id";
    public string SimilarityMetric { get; set; } = "cosine";
    public string RefreshMode { get; set; } = "wait_for";
    public int DefaultTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Embedding dimension at index-creation time. Defaults to 1536 — the size of OpenAI's
    /// ada-002 / text-embedding-3-small, the most common production embedding. Users with
    /// different embedding models override; the first Upsert also auto-discovers when this
    /// is left at null.
    /// </summary>
    public int? Dimension { get; set; } = 1536;
    public string? ApiKey { get; set; } = null;
    public string? Username { get; set; } = null;
    public string? Password { get; set; } = null;
    public bool DisableIndexAutoCreate { get; set; } = false;

    /// <summary>
    /// <c>IAdapterOptions</c> default-only page-size fallback (NOT a cap). Unified across both
    /// search-engine connectors per DATA-0103.
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;
}
