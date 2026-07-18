using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;

namespace Koan.Data.Vector.Connector.Weaviate;

public sealed class WeaviateOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default

    [Required]
    public string Endpoint { get; set; } = Infrastructure.Constants.DefaultEndpoint;
    public string? ApiKey { get; set; }

    // Index defaults
    public string Metric { get; set; } = "cosine"; // cosine|dot|l2

    // Timeout seconds for search
    public int DefaultTimeoutSeconds { get; set; } = 10;

    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

