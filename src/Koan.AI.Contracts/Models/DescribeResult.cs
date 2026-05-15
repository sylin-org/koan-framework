namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from media description.</summary>
public sealed record DescribeResult
{
    public required string Text { get; init; }
    public string? Model { get; init; }
    public Modality Modality { get; init; }
    public TimeSpan Latency { get; init; }
}
