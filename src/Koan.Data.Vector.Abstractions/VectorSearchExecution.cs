namespace Koan.Data.Vector.Abstractions;

/// <summary>Portable truth about the work used to produce a vector result.</summary>
public sealed record VectorSearchExecution(
    VectorMetric Metric,
    VectorSearchAccuracy Accuracy,
    int? CandidatesConsidered);
