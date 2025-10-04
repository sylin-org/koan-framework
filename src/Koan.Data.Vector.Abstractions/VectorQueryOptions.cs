namespace Koan.Data.Vector.Abstractions;

// Re-export or shim minimal contracts for providers to depend on. For now,
// we type-forward to Koan.Data.Abstractions to avoid churn; future move can relocate types.

public sealed record VectorQueryOptions(
    float[] Query,
    int? TopK = null,
    string? ContinuationToken = null,
    object? Filter = null,
    TimeSpan? Timeout = null,
    string? VectorName = null,
    string? SearchText = null,  // Hybrid search: text for BM25 keyword matching
    double? Alpha = null        // Hybrid search: semantic vs keyword weight (0.0=keyword, 1.0=semantic, 0.5=balanced)
);