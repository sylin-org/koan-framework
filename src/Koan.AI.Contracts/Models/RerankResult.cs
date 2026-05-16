namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from reranking.</summary>
public sealed record RerankResult
{
    public required IReadOnlyList<RankedDocument> Documents { get; init; }
    public string? Model { get; init; }
    public TimeSpan Latency { get; init; }
}
