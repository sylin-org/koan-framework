using System.ComponentModel.DataAnnotations;

namespace Sora.Data.Weaviate;

public sealed class WeaviateOptions
{
    [Required]
    public string Endpoint { get; set; } = "http://localhost:8085"; // default local dev or docker mapped
    public string? ApiKey { get; set; }

    // Index defaults
    public int Dimension { get; set; } = 384; // typical small model; must match embeddings
    public string Metric { get; set; } = "cosine"; // cosine|dot|l2

    // Guardrails
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;

    // Timeout seconds for search
    public int DefaultTimeoutSeconds { get; set; } = 10;
}
