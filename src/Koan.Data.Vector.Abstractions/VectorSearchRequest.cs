using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Abstractions;

/// <summary>Validated provider-neutral query values consumed by a vector adapter.</summary>
public sealed record VectorSearchRequest(
    ReadOnlyMemory<float> Embedding,
    int Top,
    Filter? Filter = null,
    string? Space = null,
    double? MinimumSimilarity = null,
    string? Text = null,
    double? SemanticWeight = null,
    string? Continuation = null);
