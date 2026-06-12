using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;

namespace Koan.Data.Connector.OpenSearch;

public sealed class OpenSearchOptions : IAdapterOptions
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

    // IAdapterOptions implementation - default page size fallback (NOT a cap)
    public int DefaultPageSize { get; set; } = 50;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

