using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;

namespace Koan.Data.ElasticSearch;

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
    public int? Dimension { get; set; } = null;
    public string? ApiKey { get; set; } = null;
    public string? Username { get; set; } = null;
    public string? Password { get; set; } = null;
    public bool DisableIndexAutoCreate { get; set; } = false;

    // Query configuration for vector similarity search
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;

    // IAdapterOptions implementation - map to vector-specific properties
    public int DefaultPageSize
    {
        get => DefaultTopK;
        set => DefaultTopK = value;
    }
    public int MaxPageSize
    {
        get => MaxTopK;
        set => MaxTopK = value;
    }

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}
