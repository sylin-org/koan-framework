using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Weaviate;

public sealed class WeaviateOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    [Required]
    public string Endpoint { get; set; } = "http://localhost:8085"; // default local dev or docker mapped
    public string? ApiKey { get; set; }

    // Index defaults
    public int Dimension { get; set; } = 384; // typical small model; must match embeddings
    public string Metric { get; set; } = "cosine"; // cosine|dot|l2

    // Vector-search domain knobs (NOT row-page caps). MaxTopK stays — it's a real cost
    // bound for nearest-neighbour scoring.
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;

    // IAdapterOptions implementation — default-only fallback aliased to DefaultTopK.
    public int DefaultPageSize
    {
        get => DefaultTopK;
        set => DefaultTopK = value;
    }

    // Timeout seconds for search
    public int DefaultTimeoutSeconds { get; set; } = 10;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

