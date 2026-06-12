using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;

namespace Koan.Data.Connector.ElasticSearch;

public sealed class ElasticSearchOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

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

    // Query configuration for vector similarity search. MaxTopK is a vector-search domain
    // concept (cost of nearest-neighbour scoring), not a row-page cap; it stays.
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;

    // IAdapterOptions implementation — default-only fallback. Aliased to DefaultTopK because
    // a "page" of vector results IS a top-K query for this adapter family.
    public int DefaultPageSize
    {
        get => DefaultTopK;
        set => DefaultTopK = value;
    }

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

