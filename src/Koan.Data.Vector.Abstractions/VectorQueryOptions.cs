using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Abstractions;

public sealed record VectorQueryOptions(
    float[] Query,
    int? TopK = null,
    string? ContinuationToken = null,
    // AI-0036 §10 / DATA-0097 P1: the typed, unified Filter slot (was object?). The Vector<T>/workflow
    // facades parse string/dict/JSON into this once via VectorFilterReader; VectorFilterCoordinator
    // then validates it (residual-is-error) before any repo sees it.
    Filter? Filter = null,
    TimeSpan? Timeout = null,
    string? VectorName = null,
    string? SearchText = null,  // Hybrid search: text for BM25 keyword matching
    double? Alpha = null        // Hybrid search: semantic vs keyword weight (0.0=keyword, 1.0=semantic, 0.5=balanced)
);