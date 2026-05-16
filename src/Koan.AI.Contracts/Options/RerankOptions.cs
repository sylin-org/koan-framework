namespace Koan.AI.Contracts.Options;

/// <summary>Options for document reranking requests.</summary>
public sealed record RerankOptions
{
    /// <summary>Return only the top N results.</summary>
    public int? TopN { get; init; }

    /// <summary>Minimum relevance score threshold (0.0–1.0).</summary>
    public double? Threshold { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
